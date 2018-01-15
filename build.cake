#tool "nuget:?package=GitVersion.CommandLine"

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var solutionFile    = "./MicroElements.FileStorage.sln";
var target          = Argument("target", "Default");
var configuration   = Argument("configuration", "Release");

// NOTE: Add EnvVars PublishUrlBeta and ApiKeyBeta to Travis
var publishUrlBeta  = ArgumentOrEnvVar(Context, "PublishUrlBeta", null);
var apiKeyBeta      = ArgumentOrEnvVar(Context, "ApiKeyBeta", null);

// Returns cmd arg or env var or default value
public static string ArgumentOrEnvVar(ICakeContext context, string name, string defaultValue)
{
    return context.Argument(name, (string)null) ?? context.EnvironmentVariable(name) ?? defaultValue;
}

//////////////////////////////////////////////////////////////////////
// FILES & DIRECTORIES
//////////////////////////////////////////////////////////////////////

var rootDir                     = Directory("./");
var artifactsDir                = Directory("./artifacts");
var testResultsDir              = artifactsDir + Directory("test-results");

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(ctx =>
{
   // Executed BEFORE the first task.
   Information("Running tasks...");

   Information($"solutionFile={solutionFile}");
   Information($"target={target}");
   Information($"configuration={configuration}");
   Information($"publishUrlBeta={publishUrlBeta}");
});

Teardown(ctx =>
{
   // Executed AFTER the last task.
   Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(testResultsDir);
    CleanDirectory(artifactsDir);

    var delSettings = new DeleteDirectorySettings(){Recursive=true, Force=true};
    GetDirectories("./src/**/bin/Debug").ToList().ForEach(dir=>DeleteDirectory(dir, delSettings));
    GetDirectories("./src/**/bin/Release").ToList().ForEach(dir=>DeleteDirectory(dir, delSettings));
    //GetDirectories("./src/**/obj/Debug").ToList().ForEach(dir=>DeleteDirectory(dir, delSettings));
    //GetDirectories("./src/**/obj/Release").ToList().ForEach(dir=>DeleteDirectory(dir, delSettings));

    DeleteFiles("./src/**/*.nupkg");
});

Task("Restore")
    .IsDependentOn("Clean")    
    .Does(() =>
{	
	var settings = new DotNetCoreRestoreSettings
    {        
        Sources = new [] { "https://api.nuget.org/v3/index.json" }
    };

	//DotNetCoreRestore(solutionFile, settings);
});

Task("Build")    
    .IsDependentOn("Restore")
    .Does(() =>
{	
	var settings = new DotNetCoreBuildSettings 
    { 
        Configuration = configuration,
        ArgumentCustomization =
          args => args
            .Append("/p:SourceLinkCreate=true")
    };

	DotNetCoreBuild(solutionFile, settings);
});

Task("Test")
    .Does(() =>
{
    var test_projects = GetFiles("./src/*.Tests/*.csproj");
    foreach(var test_project in test_projects)
    {
        var testSettings = new DotNetCoreTestSettings()
        {
            Configuration = configuration,
            NoBuild = true
        };
        DotNetCoreTest(test_project.FullPath, testSettings);
    }
});

Task("CopyPackages")
    .IsDependentOn("Build")
    .Does(() =>
{
    var files = GetFiles("./src/**/*.nupkg");
    CopyFiles(files, artifactsDir);
});

Task("PublishPackages")
    .IsDependentOn("CopyPackages")
    .Does(() =>
{
    var files = GetFiles(artifactsDir.Path+"/*.nupkg");

    if(publishUrlBeta == null)
    {
        Information("PublishUrlBeta is null. PublishPackages cancelled.");
        return;
    }
    if(apiKeyBeta == null)
    {
        Information("ApiKeyBeta is null. PublishPackages cancelled.");
        return;
    }
    NuGetPush(files, new NuGetPushSettings(){
        Source = publishUrlBeta,
        ApiKey = apiKeyBeta,
    });
});

GitVersion versionInfo = null;

Task("Version")
    .Does(() => {
/*
<Project>
  <PropertyGroup>
    <VersionPrefix>1.0.5</VersionPrefix>
    <VersionSuffix>beta.2</VersionSuffix>
    <PackageReleaseNotes>Added IsExists and Delete methods.</PackageReleaseNotes>
  </PropertyGroup>
</Project>
*/
        versionInfo = GitVersion(new GitVersionSettings{ 
            OutputType = GitVersionOutput.Json,
        });
        // Update version.props
        var versionPrefix = versionInfo.MajorMinorPatch;
        var versionSuffix = versionInfo.PreReleaseTag;
        var releaseNotes = "Added IsExists and Delete methods.";
var version_props = $@"
<Project>
  <PropertyGroup>
    <VersionPrefix>{versionPrefix}</VersionPrefix>
    <VersionSuffix>{versionSuffix}</VersionSuffix>
    <PackageReleaseNotes>{releaseNotes}</PackageReleaseNotes>
  </PropertyGroup>
</Project>";

        System.IO.File.WriteAllText("./version.props", version_props);
    });

Task("Default")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("CopyPackages");

Task("Travis")
    .IsDependentOn("Version")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("CopyPackages")
    .IsDependentOn("PublishPackages");  

RunTarget(target);
