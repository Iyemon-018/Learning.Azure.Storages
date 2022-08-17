// See https://aka.ms/new-console-template for more information
using System.Text;
using Azure.Storage.Files.Shares;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

Console.WriteLine("Hello, World!");

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
        string           fileName = $"{DateTime.Now: yyyy-MM-dd_HHmmssfff}.txt";
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
}

public class AppSettings
{
    public string connectionString { get; set; }

    public string storageAccountName { get; set; }

    public string storageAccountKey { get; set; }

    public string shareName { get; set; }
}