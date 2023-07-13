using Yab.BaiduPanApi;
using CommandLine.Text;
using CommandLine;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using ShellProgressBar;
using Downloader;
using System.Net;
using System;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Extensions.Logging;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Humanizer.Bytes;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace Yab.ConsoleTool
{
    [Verb("listall", HelpText = "列出该文件夹下全部内容，包括子文件夹内容")]
    class ListAllOptions
    {
        [Option('p', "ServerPath", Required = false, HelpText = "Which folder will be enumerated? Omitted for root path")]
        public string? Serverpath { get; set; }

        [Option('n', "NoFolder", Required = false, HelpText = "Not to retrieve folder path, file path only.")]
        public bool NoFolder { get; set; }

        [Option('f', "OutputPath", Required = false, HelpText = "Output file path.")]
        public string? OutputPath { get; set; }

        [Option('r', "Filter", Required = false, HelpText = "File name filter in Regex.")]
        public string? Filter { get; set; }
    }


    [Verb("diff", HelpText = "比较远程和本地的文件夹内容差异，输出文件名")]
    class DiffOptions
    {
        [Option('p', "ServerPath", Required = false, HelpText = "Which folder will be enumerated? Omitted for root path")]
        public string? Serverpath { get; set; }

        [Option('l', "LocalPath", Required = true, HelpText = "local folder path")]
        public string? LocalPath { get; set; }

        [Option('f', "OutputPath", Required = false, HelpText = "Written updated files list to OutputPath.")]
        public string? OutputPath { get; set; }


        [Option('r', "Filter", Required = false, HelpText = "File name filter in Regex.")]
        public string? Filter { get; set; }
    }



    [Verb("download", HelpText = "从百度盘下载文件")]
    public class DownloadOptions
    {
        [Option('p', "ServerPath", Required = false, HelpText = "Which folder/file will be downloaded? Omitted for root path")]
        public string? Serverpath { get; set; }

        [Option('l', "LocalPath", Required = true, HelpText = "local folder/file path")]
        public string? LocalPath { get; set; }

        [Option('r', "Filter", Required = false, HelpText = "File name filter in Regex.")]
        public string? Filter { get; set; }
    }



    [Verb("login", HelpText = "登录百度盘")]
    class LoginOptions
    {
    }


    [Verb("Resume", HelpText = "继续之前未完成的下载")]
    class ResumeOptions
    {
    }

    public class DownloadSnapshot
    {
        public DownloadSnapshot(DownloadOptions downloadOptions, List<ValueTuple<BaiduPanFileInfo, DownloadPackage>> packages)
        {
            this.downloadOptions = downloadOptions;
            Packages = packages;
        }
        public DownloadOptions downloadOptions;
        public List<ValueTuple<BaiduPanFileInfo, DownloadPackage>> Packages;
    }

    internal class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        static BaiduPanApiClient baiduPanApiClient;
        static IConfiguration Configuration;
        static SerilogLoggerFactory serilogLoggerFactory;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.


        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint GetModuleFileName(IntPtr hModule, System.Text.StringBuilder lpFilename, int nSize);
        static readonly int MAX_PATH = 255;

        public static string GetExecutablePath()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                var sb = new System.Text.StringBuilder(MAX_PATH);
                GetModuleFileName(IntPtr.Zero, sb, MAX_PATH);
                return sb.ToString();
            }
            else
            {
                return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            }
        }

        private static void InitLogger(IConfiguration configuration)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .CreateLogger();
        }


        private static int InitClient()
        {
            var str = Configuration.GetValue<string>("AccessToken");
            if (string.IsNullOrWhiteSpace(str))
            {
                Console.WriteLine("请先执行login命令获取Access token");
                return -1;
            }

            HttpClientHandler handler = new HttpClientHandler();
            if (Configuration.GetValue<int>("UsingProxy", 1) == 0)//disable
            {
                handler.Proxy = new NoProxy();
            }

            baiduPanApiClient = new BaiduPanApiClient(str, new HttpClient(handler), serilogLoggerFactory.CreateLogger<BaiduPanApiClient>(),
                Configuration.GetValue<int>("ApiParallelismDegree", 6));
            return 0;
        }

        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            //Console.Clear();


            var p = Path.GetDirectoryName(GetExecutablePath());
            if (string.IsNullOrEmpty(p))
            {
                throw new InvalidProgramException("Fail to get current path");
            }

            Environment.SetEnvironmentVariable("APP_BASE_DIRECTORY", p);

            Configuration = new ConfigurationBuilder()
                .SetBasePath(p)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            InitLogger(Configuration);
            Log.Logger.Information("");
            Log.Logger.Information("");
            Log.Logger.Information("-------------------------------------------------------------------------------------------------------------------------------");
            Log.Logger.Information("Starting with {args}", args);
            serilogLoggerFactory = new SerilogLoggerFactory(Log.Logger);

            using var parser = new CommandLine.Parser(x => {
                x.CaseInsensitiveEnumValues = false;
                x.CaseSensitive = false;
               x.AutoHelp = true;
            });

            return await parser.ParseArguments<ListAllOptions, DiffOptions, DownloadOptions, ResumeOptions, LoginOptions>(args)
                .MapResult(
                (ListAllOptions opts) => RunListAllAndReturnExitCode(opts),
                (DiffOptions opts) => RunDiffAndReturnExitCode(opts),
                (DownloadOptions opts) => RunDownloadAndReturnExitCode(opts),
                (ResumeOptions opts) => RunResumeAndReturnExitCode(opts),
                (LoginOptions opts) => RunLoginAndReturnExitCode(opts),
                errs => Task.FromResult(-1));

        }


        static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }
        public class LoginToken
        {
            public string? url;
        }
        private static Task<int> RunLoginAndReturnExitCode(LoginOptions opts)
        {
            try
            {
                int port = 10672;
                var builder = WebApplication.CreateBuilder();
                builder.Logging.ClearProviders();
                builder.Logging.AddSerilog(Log.Logger,false);
                var app = builder.Build();

                var tcs = new TaskCompletionSource<int>();
                app.MapGet("/bk", async (HttpContext context) =>
                {
                    var p = Path.GetDirectoryName(GetExecutablePath());
                    if (string.IsNullOrEmpty(p))
                    {
                        Console.WriteLine("获取可执行文件目录失败");
                        await app.StopAsync();
                        return ;
                    }
                    var s = """
<!DOCTYPE html><html lang="en" xmlns="http://www.w3.org/1999/xhtml"><head><meta charset="utf-8" />    <title></title></head>
<body>    <script>
        fetch("/post", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify({
                url: window.location.href,
            })
        }).then(re => window.close())
    </script></body></html>
""";
                    context.Response.ContentType = MediaTypeNames.Text.Html;
                    context.Response.ContentLength = Encoding.UTF8.GetByteCount(s);
                    await context.Response.WriteAsync(s);
                });
                app.MapPost("/post", async (HttpContext context, Stream body) =>
                {

                    var sr = new StreamReader(body);
                    var content = await sr.ReadToEndAsync();
                    var lt = JsonConvert.DeserializeObject<LoginToken>(content);
                    if (lt != null)
                    {
                        var t = lt.url?.Trim();
                        if (lt.url != null)
                        {
                            lt.url = "";
                        }
                        if (!string.IsNullOrEmpty(t))
                        {
                            string token = "";
                            var ta = t.Split(new[] { '&', '=' }, StringSplitOptions.RemoveEmptyEntries);
                            for (var i = 0; i < ta.Length; i++)
                            {
                                if (string.Compare("access_token", ta[i]) == 0)
                                {
                                    token = ta[i + 1];
                                    break;
                                }
                            }
                            if (!string.IsNullOrEmpty(token))
                            {
                                AddOrUpdateAppSetting("AccessToken", token);
                                Console.WriteLine("百度盘登录成功");
                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(1000);
                                    await app.StopAsync();
                                });
                                return;
                            }
                        }
                    }
                    Console.WriteLine("百度盘登录失败，请重试。如仍然失败，请联系我们");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        await app.StopAsync();
                    });
                });

                var client_id = Configuration.GetValue<string>("client_id");
                var c = $"http://127.0.0.1:{port}/bk";
                var redirect_uri = HttpUtility.UrlEncode(c);

                var uri = $"http://openapi.baidu.com/oauth/2.0/authorize?response_type=token&client_id={client_id}&redirect_uri={redirect_uri}&scope=basic,netdisk";
                Log.Logger.Information("Opening {uri}", uri);
                OpenUrl(uri);

                Console.WriteLine("请在打开的百度网站上登录，如有错误按CTRL+C结束此程序");
                app.Run($"http://127.0.0.1:{port}");

                return Task.FromResult(0);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "RunLoginAndReturnExitCode时出错");
                throw;
            }
        }

        public static void AddOrUpdateAppSetting<T>(string key, T value)
        {
            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                string json = File.ReadAllText(filePath);
                if (json == null)
                {
                    json = "";
                }
                dynamic jsonObj = JsonConvert.DeserializeObject(json) ?? new JObject();

                var sects = key.Split(":");

                if (sects.Length>=2)
                {
                    var keyPath = key.Split(":")[1];
                    jsonObj[sects[0]][sects[1]] = value;
                }
                else
                {
                    jsonObj[sects[0]] = value; // if no sectionpath just set the value
                }

                string output = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(filePath, output);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error writing app settings | {0}", ex.Message);
            }
        }
        private static  async Task<int> RunResumeAndReturnExitCode(ResumeOptions opts)
        {
            try
            {
                if (InitClient()<0)
                {
                    return -3;
                }
                var s = Configuration.GetValue<string>("DownloadSnapshot");
                if (string.IsNullOrWhiteSpace(s))
                {
                    Console.WriteLine("未找到可继续的下载任务");
                    return 0;
                }
                var snap = JsonConvert.DeserializeObject<DownloadSnapshot>(s);
                if (snap == null)
                {
                    Console.WriteLine("解读配置文件失败，可以的话建议重新使用download下载");
                    return-1;
                }

                int ret = await DownloadWithResume(snap.downloadOptions, snap.Packages);
                if (ret == 0)
                {
                    AddOrUpdateAppSetting("DownloadSnapshot", "");
                }
                return ret;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "下载时出错");
                throw;
            }
        }

        private static string  GetLocalPath(string serverpath, string LocalPath)
        {
            string? l = null;
            if (serverpath.StartsWith('/') || serverpath.StartsWith('\\'))
            {
                l = Path.Combine(LocalPath, "." + serverpath);
            }
            else
            {
                l = Path.Combine(LocalPath, serverpath);
            }
            return Path.GetFullPath(l);
        }

        private static  Task<int> RunDownloadAndReturnExitCode(DownloadOptions opts)
        {
            try
            {
                if (InitClient() < 0)
                {
                    return Task.FromResult(-3);
                }
                return DownloadWithResume(opts, null);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "下载时出错");
                throw;
            }
        }
        private static async Task<int> DownloadWithResume(DownloadOptions opts, List<ValueTuple<BaiduPanFileInfo, DownloadPackage>>? packages)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(opts.Serverpath))
                {
                    opts.Serverpath = "/";
                }
                if (string.IsNullOrWhiteSpace(opts.LocalPath))
                {
                    Console.WriteLine("LocalPath couldn't be empty");
                    return -1;
                }
                int mem = 30 * 1024 * 1024;
                var strmem = Configuration.GetValue<string>("PerFileMemoryBuffer", "30MB");
                if(ByteSize.TryParse(strmem, out var bymem))
                {
                    mem = (int)bymem.Bytes;
                }


                using var dl = new Download(baiduPanApiClient, Configuration.GetValue<int>("DownloadParallelismDegree", 6),
                    Configuration.GetValue<int>("DownloadChunkNum", 3),
                    mem,
                    Configuration.GetValue<int>("UsingProxy", 1)==0,
                    serilogLoggerFactory.CreateLogger<Download>());

                TaskCompletionSource<int>? tcs = null;
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) =>
                {
                    tcs = new TaskCompletionSource<int>();
                    try
                    {
                        Log.Logger.Information("Cancel key pressed. Saving downloading info for resuming");
                        cts.Cancel();
                        var s = dl.Pause().ConfigureAwait(false).GetAwaiter().GetResult();
                        var snap = new DownloadSnapshot(opts, s);
                        AddOrUpdateAppSetting("DownloadSnapshot", JsonConvert.SerializeObject(snap));
                        Console.WriteLine("已经保存当前下载信息。下次下载可以使用resume命令继续");
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex, "保存DownloadSnapshot时出错");
                    }
                    tcs.SetResult(0);
                };

                //resume
                if (packages!=null)
                {
                    dl.Resume(packages);
                }
                var fsids = packages?.Select(x=>x.Item1.fs_id).ToList();


                Regex? re = null;
                if (!string.IsNullOrEmpty(opts.Filter))
                {
                    re = new Regex(opts.Filter.Trim());
                }
                try
                {
                    Console.WriteLine("获取文件列表并和本地文件对比...");
                    await foreach (var item in baiduPanApiClient.ListAllFilesAsync(opts.Serverpath, re, cts.Token))
                    {
                        if (fsids != null)
                        {
                            if (fsids.Contains(item.fs_id))
                            {
                                continue;
                            }
                        }
                        var rp = Path.GetRelativePath(opts.Serverpath, item.path);
                        var l = GetLocalPath(rp, opts.LocalPath);
                        if (!Filelookalikeidentical(item, l))
                        {
                            if (!item.isdir)
                            {
                                dl.AddDownloadFile(item, l);
                            }
                            else
                            {
                                Directory.CreateDirectory(l);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }

                try
                {
                    await dl.Finish();
                    Console.WriteLine("下载完成");
                }
                catch (TaskCanceledException)
                {
                }
                if (tcs!=null)
                {
                    await tcs.Task;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Exception when RunDownloadAndReturnExitCode");
                return -1;
            }
        }


        private static async Task<int> RunDiffAndReturnExitCode(DiffOptions opts)
        {
            try
            {
                if (InitClient() < 0)
                {
                    return -3;
                }
                if (string.IsNullOrWhiteSpace(opts.Serverpath))
                {
                    opts.Serverpath = "";
                }
                if (string.IsNullOrWhiteSpace(opts.LocalPath))
                {
                    Console.WriteLine("LocalPath couldn't be empty");
                    return -1;
                }
                Action<string> cb;
                StreamWriter? fw = null;
                if (string.IsNullOrWhiteSpace(opts.OutputPath))
                {
                    cb = x =>
                    {
                        Console.WriteLine(x);
                    };
                }
                else
                {
                    fw = new StreamWriter(opts.OutputPath);
                    cb = x =>
                    {
                        fw.WriteLine(x);
                    };
                }
                try
                {
                    Regex? re = null;
                    if (!string.IsNullOrEmpty(opts.Filter))
                    {
                        re = new Regex(opts.Filter.Trim());
                    }
                    await foreach (var item in baiduPanApiClient.ListAllFilesRecursivelyAsync(opts.Serverpath, re, default))
                    {
                        var rp = Path.GetRelativePath(opts.Serverpath, item.path);
                        var l = GetLocalPath(rp, opts.LocalPath);
                        if (!Filelookalikeidentical(item, l))
                        {
                            cb(item.path);
                        }
                    }
                }
                finally
                {
                    if (fw != null)
                    {
                        fw.Dispose();
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "RunDiffAndReturnExitCode出错");
                throw;
            }
        }

        private static bool Filelookalikeidentical(BaiduPanFileInfo item, string l)
        {
            if (item.isdir)
            {
                if (Directory.Exists(l))
                {
                    return true;
                }
            }
            else
            {
                if (File.Exists(l))
                {
                    var fi = new FileInfo(l);
                    if (fi.Length == item.size)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static async Task<int> RunListAllAndReturnExitCode(ListAllOptions opts)
        {
            try
            {
                if (InitClient() < 0)
                {
                    return -3;
                }
                if (string.IsNullOrWhiteSpace(opts.Serverpath))
                {
                    opts.Serverpath = "";
                }
                Regex? re = null;
                if (!string.IsNullOrEmpty(opts.Filter))
                {
                    re = new Regex(opts.Filter.Trim());
                }
                if (string.IsNullOrWhiteSpace(opts.OutputPath))
                {
                    if (opts.NoFolder)
                    {
                        await foreach (var item in baiduPanApiClient.ListAllFilesAsync(opts.Serverpath, re,default))
                        {
                            Console.WriteLine(item.path);
                        }
                    }
                    else
                    {
                        await foreach (var item in baiduPanApiClient.ListAllFilesRecursivelyAsync(opts.Serverpath, re, default))
                        {
                            Console.WriteLine(item.path);
                        }
                    }
                }
                else
                {
                    if (opts.NoFolder)
                    {
                        await foreach (var item in baiduPanApiClient.ListAllFilesAsync(opts.Serverpath,re, default))
                        {
                            Console.WriteLine(item.path);
                        }
                    }
                    else
                    {
                        using var fw = new StreamWriter(opts.OutputPath);
                        await foreach (var item in baiduPanApiClient.ListAllFilesRecursivelyAsync(opts.Serverpath, re, default))
                        {
                            fw.WriteLine(item.path);
                        }
                    }

                }
                return 0;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "RunListAndReturnExitCode出错");
                throw;
            }
        }
    }
}