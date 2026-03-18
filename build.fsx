#r "./lib/Tools/LSLib.dll"

open LSLib.LS
open System.IO


//cleanup old .loca files
Directory.GetFiles("./Home Brew - Comprehensive Reworks/Localization/English/", "*.loca")
|> Array.iter File.Delete

// generate new .loca files
Directory.GetFiles("./Home Brew - Comprehensive Reworks/Localization/English/", "*.xml")
|> Array.map System.IO.Path.GetFullPath
|> Array.iter (fun f ->    
    LocaUtils.Save(
        resource = LocaUtils.Load f, 
        outputPath = f.Replace(".xml", ".loca"), 
        format = LocaFormat.Loca
    )
)

// build package
Packager().CreatePackage(
    packagePath = System.IO.Path.GetFullPath "./Home Brew - Comprehensive Reworks - Lore Text.pak",
    inputPath = System.IO.Path.GetFullPath "./Home Brew - Comprehensive Reworks/" ,
    build = new PackageBuildData(
    Version = Enums.PackageVersion.V18,
    Compression = CompressionMethod.LZ4,
    Priority = 0uy
)
).Wait()
