#r "./lib/Tools/LSLib.dll"

open LSLib.LS
open System.IO
open System.Xml.Linq


//cleanup old .loca files
do Directory.GetFiles("./Home Brew - Comprehensive Reworks/Localization/English/", "*.loca")
|> Array.iter File.Delete

// generate new .loca files
do Directory.GetFiles("./Home Brew - Comprehensive Reworks/Localization/English/", "*.xml")
|> Array.map System.IO.Path.GetFullPath
|> Array.iter (fun f ->    
    LocaUtils.Save(
        resource = LocaUtils.Load f, 
        outputPath = f.Replace(".xml", ".loca"), 
        format = LocaFormat.Loca
    )
)

// get mod version from `meta.lsx`


let version64 =
    // intentionally unsafe, will crash if it can't read the version
    "./Home Brew - Comprehensive Reworks/Mods/Home Brew - Comprehensive Reworks - Lore Texts/meta.lsx"
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
let fileName = "Home Brew - Comprehensive Reworks - Lore Text-" + version64 + ".pak"
let outputPak = System.IO.Path.GetFullPath $"./{fileName}"
do File.Delete outputPak
do Packager().CreatePackage(
        packagePath = outputPak,
        inputPath = System.IO.Path.GetFullPath "./Home Brew - Comprehensive Reworks/" ,
        build = new PackageBuildData(
            Version = Enums.PackageVersion.V18,
            Compression = CompressionMethod.LZ4,
            Priority = 0uy
        )
    ).Wait()

System.Console.WriteLine $"Generated {outputPak}"