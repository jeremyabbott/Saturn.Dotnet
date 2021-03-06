open System.Text.RegularExpressions
// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#r "./packages/FAKE/tools/FakeLib.dll"
#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"

open Fake.ReleaseNotesHelper
open Fake.AssemblyInfoFile
open Fake.Git
open Fake
open System
open Octokit

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let project = "Saturn.Dotnet"

let summary = "A dotnet CLI tool for Saturn projects"

let gitOwner = "Krzysztof-Cieslak"
let gitName = "Saturn.Dotnet"
let gitHome = "https://github.com/" + gitOwner


// --------------------------------------------------------------------------------------
// Build variables
// --------------------------------------------------------------------------------------

let buildDir  = FullName "./build/"
let dotnetcliVersion = "2.1.3"

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES.md")

// --------------------------------------------------------------------------------------
// Build Targets
// --------------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDirs [buildDir]
)

Target "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ Attribute.Title projectName
          Attribute.Product project
          Attribute.Description summary
          Attribute.Version release.AssemblyVersion
          Attribute.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath,
          projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName)
        )

    !! "src/**/*.??proj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, _, folderName, attributes) ->
        match projFileName with
        | Fsproj -> CreateFSharpAssemblyInfo (folderName </> "AssemblyInfo.fs") attributes
        | Csproj -> CreateCSharpAssemblyInfo ((folderName </> "Properties") </> "AssemblyInfo.cs") attributes
        | Vbproj -> CreateVisualBasicAssemblyInfo ((folderName </> "My Project") </> "AssemblyInfo.vb") attributes
        | Shproj -> ()
        )
)

Target "InstallDotNetCLI" (fun _ ->
    DotNetCli.InstallDotNetSDK dotnetcliVersion |> ignore
)

Target "Restore" (fun _ ->
    DotNetCli.Restore id
)

Target "Build" (fun _ ->
    DotNetCli.Build id
)

// --------------------------------------------------------------------------------------
// Release Targets
// --------------------------------------------------------------------------------------



Target "Pack" (fun _ ->
    DotNetCli.Pack (fun p ->
        { p with
            WorkingDir = "./src/Saturn.Dotnet"
            Configuration = "Release";
            OutputPath = buildDir;
            AdditionalArgs = [sprintf "/p:Version=%s" release.NugetVersion ]
        }
    )
)

Target "ReleaseGitHub" (fun _ ->
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion

    let client =
        let user =
            match getBuildParam "github-user" with
            | s when not (String.IsNullOrWhiteSpace s) -> s
            | _ -> getUserInput "Username: "
        let pw =
            match getBuildParam "github-pw" with
            | s when not (String.IsNullOrWhiteSpace s) -> s
            | _ -> getUserPassword "Password: "

        createClient user pw

    // release on github
    client
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    |> releaseDraft
    |> Async.RunSynchronously
)

Target "Push" (fun _ ->
    let key =
        match getBuildParam "nuget-key" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "NuGet Key: "
    Paket.Push (fun p -> { p with WorkingDir = buildDir; ApiKey = key }))

// --------------------------------------------------------------------------------------
// Build order
// --------------------------------------------------------------------------------------
Target "Default" DoNothing
Target "Release" DoNothing

"Clean"
  ==> "InstallDotNetCLI"
  ==> "AssemblyInfo"
  ==> "Restore"
  ==> "Build"
  ==> "Default"

"Default"
  ==> "Pack"
  ==> "ReleaseGitHub"
  ==> "Push"
  ==> "Release"

RunTargetOrDefault "Default"
