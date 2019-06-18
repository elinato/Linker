#tool nuget:?package=GitVersion.CommandLine&version=4.0.0-beta0012
#tool nuget:?package=OctopusTools&version=6.7.0

#addin nuget:?package=Cake.Curl&version=4.1.0
#addin nuget:?package=Cake.Npm&version=0.17.0

#load build/paths.cake
#load build/version.cake
#load build/package.cake
#load build/urls.cake

var task = Argument("Task", "Build");

Setup<PackageMetadata>(contect => {
    return new PackageMetadata(
        outputDirectory: Argument("packageOutputDirectory", "packages"),
        name: "Linker-2.1"
    );
});

Task("Compile")
    .Does(() =>
{
    DotNetCoreBuild(Paths.SolutionFile.FullPath);
});

Task("Test")
    .IsDependentOn("Compile")
    .Does(() =>
{
    DotNetCoreTest(Paths.TestProjectFile.FullPath);
});

Task("Version")
    .Does<PackageMetadata>(package =>
{
     package.Version = ReadVersionFromProjectFile(Context);

    if (package.Version == null)
    {
        Information("Project version is missing");
        package.Version = GitVersion().FullSemVer;
    }
    
    Information($"Calculated version number {package.Version}");
});

Task("Build-Frontend")
    .Does(() =>
{
    NpmInstall(settings => settings.FromPath(Paths.FrontendDirectory));
    NpmRunScript("build", settings => settings.FromPath(Paths.FrontendDirectory));
});

Task("Package-Zip")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package =>
{
    CleanDirectory(package.OutputDirectory);
    package.Extension = "zip";

    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings
            {
                NoLogo = true
            }
        });
    Zip(Paths.PublishDirectory, package.FullPath);
});

Task("Package-Octopus")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package =>
{
    CleanDirectory(package.OutputDirectory);
    package.Extension = "nupkg";

    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings
            {
                NoLogo = true
            }
        });
        OctoPack(package.Name, new OctopusPackSettings{
            Format = OctopusPackFormat.NuPkg,
            Version = package.Version,
            BasePath = Paths.PublishDirectory,
            OutFolder = package.OutputDirectory
        });
});

Task("Deploy-Kudu")
    .Description("Deploy to Kudu")
    .IsDependentOn("Package-Zip")
    .Does<PackageMetadata>(package =>
{
    CurlUploadFile(
        package.FullPath,
        Urls.KuduDeploymentUrl,
        new CurlSettings
        {
            Username = EnvironmentVariable("DeploymentUser"),
            Password = EnvironmentVariable("DeploymentPassword"),
            RequestCommand = "POST",
            ProgressBar = true,
            ArgumentCustomization = args => args.Append("--fail")
        }
    );
});

Task("Deploy-Octopus")
.IsDependentOn("Package-Octopus")
    .Does<PackageMetadata>(package =>
{
    OctoPush(
        Urls.OctopusDeployUrl.AbsoluteUri,
        EnvironmentVariable("OctopusApiKey"),
        package.FullPath,
        new OctopusPushSettings
        {
            EnableServiceMessages = true
        }
    );
    OctoCreateRelease(
        "Linker-2",
        new CreateReleaseSettings
        {
            Server = Urls.OctopusDeployUrl.AbsoluteUri,
            ApiKey = EnvironmentVariable("OctopusApiKey"),
            ReleaseNumber = package.Version,
            DefaultPackageVersion = package.Version,
            DeployTo = "Test",
            IgnoreExisting = true,
            DeploymentProgress = true,
            WaitForDeployment = true
        }
    );
});

RunTarget(task);