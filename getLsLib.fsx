module LSLibHelpers

// Load necessary namespaces
open System
open System.Net.Http
open System.IO
open System.IO.Compression
open System.Text.Json

module private P = 

    // Helper function to download the most recent release of ExportTool
    let downloadLatestRelease (url: string) (destinationPath: string) = 
        task {
            use client = new HttpClient()
            let! response = client.GetAsync(url)
            let response = response.EnsureSuccessStatusCode()
            use! stream = response.Content.ReadAsStreamAsync()
            use fileStream = new FileStream(destinationPath, FileMode.Create)
            do! stream.CopyToAsync fileStream
        }

    // Helper function to extract a .zip file to a folder
    let extractAndDeleteZip (zipFilePath: string) (extractPath: string) =
        ZipFile.ExtractToDirectory(zipFilePath, extractPath)
        File.Delete zipFilePath


    // Helper function to get the URL of the latest release from GitHub using System.Text.Json
    let getLatestReleaseUrl lsLibRepo =
        task {
            use client = new HttpClient()
            client.DefaultRequestHeaders.UserAgent.Add (Headers.ProductInfoHeaderValue("Mozilla","5.0"))
            let url = $"https://api.github.com/repos/{lsLibRepo}/releases/latest"
            let! response = client.GetStringAsync url
            
            // Parse JSON response using System.Text.Json        
            return response
                |> JsonDocument.Parse
                |> _.RootElement
                    .GetProperty("assets")
                    .EnumerateArray()
                |> Seq.head
                |> _.GetProperty("browser_download_url")
                    .GetString()
        }

open P

/// Download and extract LSLib ExportTool to a temp folder
let downloadToolsLsLib lsLibRepo targetParentPath =
    task {
        // Get the URL of the latest release
        let! downloadUrl = getLatestReleaseUrl lsLibRepo
        
        let zipFilePath = Path.Combine(targetParentPath, "ExportTool.zip")

        // Download the latest release
        printfn "Downloading ExportTool from %s" downloadUrl
        do! downloadLatestRelease downloadUrl zipFilePath
        printfn "Download complete."

        // Extract the .zip file
        let extractionPath = Path.Combine (targetParentPath, "tmp")
        printfn "Extracting .zip file..."
        extractAndDeleteZip zipFilePath extractionPath
        printfn "Extraction complete."

        // Move the Tools subfolder to current path        
        let toolsFolder = Path.Combine(extractionPath, "Packed", "Tools")
        if not (Directory.Exists toolsFolder) then failwithf "Tools subfolder not found in %s" extractionPath
        Directory.Move (toolsFolder, Path.Combine(targetParentPath, "Tools"))
        Directory.Delete(extractionPath, recursive = true)
    }