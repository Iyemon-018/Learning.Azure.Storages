// See https://aka.ms/new-console-template for more information

using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Azure.Storage.Files.Shares.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var console = ConsoleApp.CreateBuilder(args)
                        .ConfigureServices((hostContext, services) =>
                         {
                             services.Configure<AppSettings>(hostContext.Configuration);
                         })
                        .Build();
console.AddCommands<AzureFilesRunner>();
console.Run();

public class AzureFilesRunner : ConsoleAppBase
{
    private readonly AppSettings _appSettings;

    public AzureFilesRunner(IOptions<AppSettings> options)
    {
        _appSettings = options.Value;
    }

    // c.f. https://docs.microsoft.com/ja-jp/azure/storage/files/storage-dotnet-how-to-use-files?tabs=dotnet#access-the-file-share-programmatically
    //
    // e.g. > Learning.Azure.Storages.exe upload --message "Write test content."
    //
    public async Task Upload(string message = "Learning Azure Files upload files.")
    {
        // 1. Files にアクセスするためのクライアントを作成する。
        // ShareClient に渡すのは接続文字列と共有名のみでいい。
        ShareClient share = new ShareClient(_appSettings.connectionString, _appSettings.shareName);

        // 共有がなければ作る。実際のアプリではこれは必要ない場合もあると思う。
        await share.CreateIfNotExistsAsync();

        // 存在チェックはこんな感じ。
        if (!await share.ExistsAsync()) return;

        Console.WriteLine($"Share created: {share.Name}");

        // 2. 共有内のディレクトリを構成する。
        // とりあえず Logs というフォルダを作ってその配下にファイルを配置する。
        // .CreateIfNotExistsAsync() は共有・ディレクトリ・ファイルのいずれでも使用することができる。
        ShareDirectoryClient? directory = share.GetDirectoryClient("Logs");
        await directory.CreateIfNotExistsAsync();

        if (!await directory.ExistsAsync()) return;

        // 3. ファイルクライアントを作ってアップロードする。
        // ここではローカルで作るファイルとアップロードするファイル名は一致させているけど、別名でもOK。
        // まずは ShareFileClient オブジェクトを作っておく。
        // note: Files にアップロードするファイルは先頭が数値だと自動的に" "(半角スペース)が含まれる。
        //       ローカルで"2022-08-10_001122333.txt"というファイル名だとしても、Azure Files にアップロードすると
        //       " 2022-08-10_001122333.txt"になる。これは Files の仕様？
        //       先頭がアルファベットなら発生しない。先頭が数値以外でも発生するかどうかは不明。
        string fileName = $"test-{DateTime.Now:yyyy-MM-dd_HHmmssfff}.txt";
        ShareFileClient? file     = directory.GetFileClient(fileName);
        await file.DeleteIfExistsAsync();

        // ローカルにファイルを作ってからそれを読み込んでアップロードする。
        // .UploadAsync(Stream) でアップロードできるので、ファイルを作らなくても Stream であればいい。
        // サンプルなのでコマンドライン引数でテキストの内容は変更できるようにしている。
        await File.WriteAllTextAsync(fileName, message);
        await using FileStream stream = File.OpenRead(fileName);

        // .CreateAsync(length) と .Upload(Stream) はセットで考える。
        await file.CreateAsync(stream.Length);
        await file.UploadAsync(stream);
    }

    public async Task UploadDir(string source, string destination)
    {
        ShareClient share = new ShareClient(_appSettings.connectionString, _appSettings.shareName);

        ShareDirectoryClient directoryClient = share.GetDirectoryClient(destination);

        // ディレクトリの捜索とアップロードを並行して行う。
        await UploadDirRecurse(source, directoryClient);
    }

    private async Task UploadDirRecurse(string source, ShareDirectoryClient directoryClient)
    {
        // 予め指定されたディレクトリを作成しないと、このディレクトリ配下のファイルを作成することができない。
        await directoryClient.CreateIfNotExistsAsync();

        foreach (var file in Directory.GetFiles(source))
        {
            // このディレクトリ配下のファイルをすべてアップロードする。
            string fileName = Path.GetFileName(file);

            ShareFileClient fileClient = directoryClient.GetFileClient(fileName);

            await using FileStream stream = File.OpenRead(file);

            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            // サブディレクトリがあれば同じように作る。
            // ここで、サブディレクトリが空であってもこのメソッドの最上位の .CreateIfNotExistsAsync が呼ばれるので空フォルダの作り忘れがなくなる。
            var directoryInfo = new DirectoryInfo(directory);
            await UploadDirRecurse(directory, directoryClient.GetSubdirectoryClient(directoryInfo.Name));
        }
    }

    // e.g. > Learning.Azure.Storages.exe download --directory "Logs" --file-name " 2022-08-17_235643613.txt" --output "2022-08-17_235643613.txt"
    public async Task Download(string directory, string fileName, string output)
    {
        // ダウンロードはアップロードと下準備はほぼ同じ。
        ShareClient share = new ShareClient(_appSettings.connectionString, _appSettings.shareName);

        if (!await share.ExistsAsync()) return;

        ShareDirectoryClient? directoryClient = share.GetDirectoryClient(directory);
        if (!await directoryClient.ExistsAsync())
        {
            Console.WriteLine($"Directory not found [{directory}].");
            return;
        }
        
        // 何故か .GetFileClient(fileName) の fileName の先頭に" "(半角スペース)を入れないとファイルが見つからない。
        // 原因がよくわからない。
        // ファイル名の先頭が数値だから…？
        ShareFileClient? file = directoryClient.GetFileClient(fileName);
        if (!await file.ExistsAsync())
        {
            Console.WriteLine($"File not found [{Path.Combine(directory, fileName)}].");
            return;
        }

        Console.WriteLine($"Downloading [{Path.Combine(directory, fileName)}].");

        // ファイルはダウンロードしてから Stream として出力する。
        Response<ShareFileDownloadInfo>? download = await file.DownloadAsync();
        
        await using FileStream fileStream = File.OpenWrite(output);
        await download.Value.Content.CopyToAsync(fileStream);
        await fileStream.FlushAsync();

        fileStream.Close();

        Console.WriteLine($"File download completed. > {output}");
    }
    

    // e.g. > Learning.Azure.Storages.exe download-dir --directory "Logs" --destination .
    public async Task DownloadDir(string directory, string destination)
    {
        // ダウンロードはアップロードと下準備はほぼ同じ。
        ShareClient share = new ShareClient(_appSettings.connectionString, _appSettings.shareName);

        if (!await share.ExistsAsync()) return;

        ShareDirectoryClient? directoryClient = share.GetDirectoryClient(directory);
        if (!await directoryClient.ExistsAsync())
        {
            Console.WriteLine($"Directory not found [{directory}].");
            return;
        }

        await DownloadFiles(directoryClient, destination);
    }

    private async Task DownloadFiles(ShareDirectoryClient directoryClient, string destination)
    {
        if (!directoryClient.GetFilesAndDirectories().Any())
        {

            // ディレクトリ内が空のケース
            // このフォルダもコピーしたい場合はこれが必要になる。
            var output = Path.Combine(destination, directoryClient.Path);

            Console.WriteLine($"空のディレクトリ[{directoryClient.Path}]を[{output}]へ作成します。");

            if (!Directory.Exists(output)) Directory.CreateDirectory(output);
            return;
        }

        await foreach (var item in directoryClient.GetFilesAndDirectoriesAsync())
        {
            if (item.IsDirectory)
            {
                // 再帰してサブディレクトリをコピーする
                await DownloadFiles(directoryClient.GetSubdirectoryClient(item.Name), destination);
            }
            else
            {
                var fileClient = directoryClient.GetFileClient(item.Name);
                if (await fileClient.ExistsAsync())
                {
                    // ディレクトリごとコピーするのでフォルダがなければ作る必要あり。
                    var output = Path.Combine(destination, fileClient.Path);
                    var dir    = Path.GetDirectoryName(output);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    Console.WriteLine($"[{fileClient.Path}]から[{output}]へコピーを開始します。");

                    // ファイルはダウンロードしてから Stream として出力する。
                    Response<ShareFileDownloadInfo>? download = await fileClient.DownloadAsync();

                    await using var fileStream = File.OpenWrite(output);
                    await download.Value.Content.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();

                    fileStream.Close();

                    Console.WriteLine($"[{fileClient.Path}]から[{output}]へコピーを完了しました。");
                }
            }
        }
    }

    // 指定したパスのファイル、フォルダ名を列挙する。
    // e.g. > Learning.Azure.Storages.exe dir --path "Logs"
    public async Task Dir(string path)
    {
        ShareDirectoryClient directory = new ShareDirectoryClient(_appSettings.connectionString, _appSettings.shareName, path);
        if (!await directory.ExistsAsync())
        {
            Console.WriteLine($"Failed: Directory not found [{path}].");
            return;
        }

        // ファイルとフォルダを取得する場合は .GetFilesAndDirectoriesAsync() でできる。
        // 第一引数で prefix を指定することもできる。
        // prefix 以外は指定することができないので suffix やもっと細かい条件はループ内で行う必要がある。
        await foreach (ShareFileItem? item in directory.GetFilesAndDirectoriesAsync())
        {
            string? fullPath = item.Name;
            if (!item.IsDirectory)
            {
                ShareFileClient? file = directory.GetFileClient(item.Name);
                fullPath = file.Path;
            }

            Console.WriteLine($"- [{fullPath}]");
        }
    }

    // 同一共有名内でファイル コピーする。
    // e.g. > Learning.Azure.Storages.exe copy-file --source "Logs/ 2022-08-18_234830982.txt" --destination "Logs/_2022-08-18_234830982.txt"
    public async Task CopyFile(string source, string destination)
    {
        // パスさえ指定できれば直接ファイル クライアントを作ることもできる。
        ShareFileClient src = new ShareFileClient(_appSettings.connectionString, _appSettings.shareName, source);

        if (!await src.ExistsAsync())
        {
            Console.WriteLine($"Failed: File not found [{source}].");
            return;
        }

        // コピー先のファイル クライアントから親ディレクトリ クライアントを参照する。
        // コピー先のディレクトリが存在するかどうかのチェックはこれでできる。
        // やろうと思えば別共有名に移動することもできそう。(接続文字列が同じであれば）
        ShareFileClient       dest   = new ShareFileClient(_appSettings.connectionString, _appSettings.shareName, destination);
        ShareDirectoryClient? parent = dest.GetParentShareDirectoryClient();
        if (!await parent.ExistsAsync())
        {
            Console.WriteLine($"Failed: Destination directory not found [{parent.Path}].");
            return;
        }

        // コピーは .StartCopyAsync でできる。コピー元は Uri を指定する。
        await dest.StartCopyAsync(src.Uri);

        if (!await dest.ExistsAsync())
        {
            Console.WriteLine($"Failed: File not copied [{dest}].");
            return;
        }

        Console.WriteLine($"Success: File copied. [{source}] -> [{destination}]");
    }
}

public class AppSettings
{
    public string connectionString { get; set; }

    public string storageAccountName { get; set; }

    public string storageAccountKey { get; set; }

    public string shareName { get; set; }
}