#r "./lib/Tools/LSLib.dll"

open LSLib.LS

let testFile = "./Localization/English/Classes Reworked (Sorcerer).xml"
let resource = LocaUtils.Load testFile

LocaUtils.Save(resource, testFile.Replace(".xml", ".loca"), LocaFormat.Loca)

let build = new PackageBuildData(
    Version = Enums.PackageVersion.V18,
    Compression = CompressionMethod.LZ4,
    Priority = 0uy
)

let packager = new Packager()

packager.CreatePackage(
    packagePath = "./Home Brew - Comprehensive Reworks - Lore Text.pak",
    inputPath = "./Home Brew - Comprehensive Reworks/",
    build = build
)
