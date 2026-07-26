open System
open System.IO
open System.IO.Compression

#load "./getLsLib.fsx"

if not (Directory.Exists "./Tools") then
    (LSLibHelpers.downloadToolsLsLib "Norbyte/lslib" ".").Wait()    

#r "./Tools/LSLib.dll"

open LSLib.LS

let upstreamModName = "Home Brew - Comprehensive Reworks"
let upstreamModNameSubfolder = "HomeBrew - Comprehensive Reworks"
let modName = "HomeBrew - Comprehensive Reworks - Lore Texts"

module Localization = 

    let upstreamModLocalizationFolder= $"./{upstreamModName}/Mods/{upstreamModNameSubfolder}/Localization/English/"

    let private tempDir = 
        $"./tmp"
        |> Path.GetFullPath

    let private generatedXmlPath = 
        $"./{upstreamModName}/Mods/{modName}/Localization/English/{modName}_generated_{DateTime.UtcNow.Ticks}.xml"
        |> Path.GetFullPath
    let private generatedLocaPath = generatedXmlPath.Replace(".xml", ".loca")

    let private moveXmlsToTemp () =
        do Directory.CreateDirectory(tempDir) |> ignore
        do Directory.GetFiles(upstreamModLocalizationFolder, "*.xml")
        |> Array.iter (fun f -> File.Move(f, Path.Combine(tempDir, Path.GetFileName(f))))

    let private collectLoreLines () =
        let lines =
            Directory.GetFiles(tempDir, "*.xml")
            |> Array.collect (fun f ->
                File.ReadAllLines(f)
                |> Array.where _.Contains("loreTexts=\"true\""))
        let content =
            [| yield """<?xml version="1.0" encoding="utf-8"?>"""
               yield "<contentList>"
               yield! lines
               yield "</contentList>" |]
        // test for validity
        Xml.Linq.XDocument.Parse (String.concat "\n" content) |> ignore

        // write out
        do generatedXmlPath |> Path.GetDirectoryName |> Directory.CreateDirectory |> ignore
        File.WriteAllLines(generatedXmlPath, content)

    let beforeBuild() =
        do moveXmlsToTemp()
        do collectLoreLines()
        LocaUtils.Save(
            resource = LocaUtils.Load generatedXmlPath,
            outputPath = generatedLocaPath,
            format = LocaFormat.Loca
        )

    let afterBuild() =
        do File.Delete generatedXmlPath
        do File.Delete generatedLocaPath
        do Directory.GetFiles(tempDir, "*.xml")
        |> Array.iter (fun f -> File.Move(f, Path.Combine(upstreamModLocalizationFolder, Path.GetFileName f)))
        do Directory.Delete tempDir

// helper
let zipFile (sourceFile: string) (zipPath: string) =
    use archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)

    let entryName = Path.GetFileName(sourceFile)
    archive.CreateEntryFromFile(sourceFile, entryName, CompressionLevel.Optimal)
    |> ignore

// get mod version from `meta.lsx`
open System.Xml.Linq
let version =
    // intentionally unsafe, will crash if it can't read the version
    $"./{upstreamModName}/Mods/{modName}/meta.lsx"
    |> XDocument.Load    
    |> _.Descendants()
        |> Seq.find (fun n -> 
            n.Name = XName.Get "node" 
            && n.Attribute(XName.Get "id").Value = "ModuleInfo")
    |> _.Elements()
        |> Seq.find (fun a -> 
            a.Name = XName.Get "attribute" 
            && a.Attribute(XName.Get "id").Value = "Version64")
    
    |> _.Attribute(XName.Get "value").Value
    |> System.Int64.Parse
    |> LSLib.LS.PackedVersion.FromInt64
    |> fun pv -> sprintf "%i.%i.%i.%i" pv.Major pv.Minor pv.Revision pv.Build


let outputPak, outputZip = 
    // build package
    let outputBase = 
        Directory.CreateDirectory "./output"
        |> _.FullName
        |> fun path -> $"{path}/{modName}"

    in outputBase + ".pak", outputBase + $"-{version}.zip"


// actual build
do File.Delete outputPak
do Localization.beforeBuild()
do Packager().CreatePackage(
        packagePath = outputPak,
        inputPath = System.IO.Path.GetFullPath $"./{upstreamModName}/" ,
        build = new PackageBuildData(
            Version = Enums.PackageVersion.V18,
            Compression = CompressionMethod.LZ4,
            Priority = 0uy
        )
    ).Wait()
do Localization.afterBuild()

// zip into versioned file
do zipFile outputPak outputZip

System.Console.WriteLine $"Generated {outputPak}"