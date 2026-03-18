open System.IO

#load "./getLsLib.fsx"

if not (Directory.Exists "./Tools") then
    (LSLibHelpers.downloadToolsLsLib "Norbyte/lslib" ".").Wait()    

#r "./Tools/LSLib.dll"

open LSLib.LS

let upstreamModName = "Home Brew - Comprehensive Reworks"
let modName = 
    $"./{upstreamModName}/Mods/"
    |> Directory.GetDirectories
    |> Array.exactlyOne
    |> Path.GetFileName

module Localization = 

    // cleanup old .loca files
    let cleanupLocaFiles() = 
        do Directory.GetFiles($"./{upstreamModName}/Localization/English/", "*.loca")
        |> Array.iter File.Delete

    let hideUnaffectedFiles () = 
        do Directory.GetFiles($"./{upstreamModName}/Localization/English/", "*.xml")
        |> Array.where (File.ReadAllText >> _.Contains("loreTexts=\"true\"")>> not)
        |> Array.iter (fun f -> File.Move(f, f.Replace(".xml", ".definitelynotanxmlfile")))

    // rename mod files to get them loaded after HB originals
    let renameFiles oldSubstring newSubstring =
        do Directory.GetFiles $"./{upstreamModName}/Localization/English/"
        |> Array.map System.IO.Path.GetFullPath
        |> Array.iter (fun f ->    

            let newName = f.Replace(oldValue = oldSubstring, newValue = newSubstring)
            // add a suffix 
            File.Move(f, newName)
        )

    let beforeBuild() = 
        // rename mod files and generate new .loca files
        do cleanupLocaFiles()
        do hideUnaffectedFiles()
        do renameFiles ".xml" ".loretext.xml"
        do Directory.GetFiles($"./{upstreamModName}/Localization/English/", "*.xml")
        |> Array.map System.IO.Path.GetFullPath
        |> Array.iter (fun f ->    

            LocaUtils.Save(
                resource = LocaUtils.Load f, 
                outputPath = f.Replace(".xml", ".loca"), 
                format = LocaFormat.Loca
            )
        )

    let afterBuild() = 
        // cleanup, restore xml file names
        do renameFiles ".loretext.xml" ".xml" 
        do renameFiles ".definitelynotanxmlfile" ".xml" 
        do cleanupLocaFiles()

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

// build package
let outputPath = 
    Directory.CreateDirectory "./output"
    |> _.FullName
    |> fun path -> $"{path}/{modName}-{version}.pak"

// actual build
do File.Delete outputPath
do Localization.beforeBuild()
do Packager().CreatePackage(
        packagePath = outputPath,
        inputPath = System.IO.Path.GetFullPath $"./{upstreamModName}/" ,
        build = new PackageBuildData(
            Version = Enums.PackageVersion.V18,
            Compression = CompressionMethod.LZ4,
            Priority = 0uy
        )
    ).Wait()
do Localization.afterBuild()

System.Console.WriteLine $"Generated {outputPath}"