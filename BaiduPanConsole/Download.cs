using BaiduPanApi;
using Downloader;
using Humanizer;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog.Core;
using ShellProgressBar;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Humanizer.Bytes;
using System.Runtime.InteropServices;

namespace Yab.ConsoleTool
{
    internal class Download:IDisposable
    {
        BaiduPanApiClient panApiClient;
        ActionBlock<ValueTuple<BaiduPanFileInfo, string, DownloadPackage?>> downloadqueue;

        //Timer ProgbarUpdate;
        ProgressBar? ConsoleProgress;
        ProgressBarOptions ChildOption;
        ProgressBarOptions ProcessBarOption;
        ulong totalsize;
        ulong completedsize;

        DownloadConfiguration CurrentDownloadConfiguration;
        CancellationTokenSource cancellationSource;

        ILogger<Download> Logger;

        public Download(BaiduPanApi.BaiduPanApiClient baiduPanApiClient,int concurrentdownload, int chunknum,int memory,bool disableproxy,ILogger<Download> logger1)
        {
            Logger = logger1;
            cancellationSource = new CancellationTokenSource();
            panApiClient = baiduPanApiClient;

            downloadqueue = new ActionBlock<ValueTuple<BaiduPanFileInfo, string, DownloadPackage?>>(DownloadOneFile,
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = concurrentdownload, CancellationToken= cancellationSource.Token });

            CurrentDownloadConfiguration = new DownloadConfiguration()
            {
                MaximumMemoryBufferBytes = memory,
                ChunkCount = chunknum, // file parts to download, default value is 1
                ParallelDownload = true, // download parts of file as parallel or not. Default value is false,
                MaxTryAgainOnFailover = 5,
                ParallelCount = chunknum,
                // clear package chunks data when download completed with failure, default value is false
                ClearPackageOnCompletionWithFailure = true,
                // minimum size of chunking to download a file in multiple parts, default value is 512
                MinimumSizeOfChunking =8L* 1024*1024,
                RequestConfiguration =
                {
                    KeepAlive = true, // default value is false
                    UserAgent="pan.baidu.com",
                }
            };
            if (disableproxy)
            {
                CurrentDownloadConfiguration.RequestConfiguration.Proxy = new NoProxy();
            }
            ProcessBarOption = new ProgressBarOptions()
            {
                ForegroundColor = ConsoleColor.Blue,
                ForegroundColorDone = ConsoleColor.DarkGreen,
                BackgroundColor = ConsoleColor.DarkGray,
                BackgroundCharacter = '=',
                ProgressBarOnBottom = false,
                //DisplayTimeInRealTime = false,
                ProgressCharacter = '>',
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ProcessBarOption.EnableTaskBarProgress = true;
            }
            ChildOption = new ProgressBarOptions
            {
                ForegroundColor = ConsoleColor.Green,
                BackgroundColor = ConsoleColor.DarkGray,
                ProgressCharacter = '\u2593',
                ProgressBarOnBottom = true,
                CollapseWhenFinished = true
            };


            //
        }
        public bool IsChinese(string text)
        {
            return text.Any(c => (uint)c >= 0x4E00 && (uint)c <= 0x2FA1F);
        }
        private async Task DownloadOneFile(ValueTuple<BaiduPanFileInfo, string, DownloadPackage?> tuple)
        {
            try
            {
                if (totalsize == 0)
                {
                    Interlocked.Add(ref totalsize, 1);
                }

                ProgressBar? mainprogressBar = null;
                lock (this)
                {
                    if (ConsoleProgress==null)
                    {
                        ConsoleProgress = new ProgressBar(10000, 
                            $"{((long)completedsize).Bytes().Humanize()}/{((long)totalsize).Bytes().Humanize()}", ProcessBarOption);
                    }
                    mainprogressBar = ConsoleProgress;
                }
                var downloader = new DownloadService(CurrentDownloadConfiguration);

                string subtitle = tuple.Item1.path;
                int width = Console.WindowWidth - 18;
                if (width>10)
                {
                    if (IsChinese(tuple.Item1.path))
                    {
                        width /= 2;
                    }
                    subtitle = tuple.Item1.path.Truncate(width, Truncator.FixedLength,TruncateFrom.Left);
                }
                else
                {
                    subtitle = tuple.Item1.path;
                }
                using var progress = mainprogressBar.Spawn(10000, subtitle.Trim(), ChildOption);
                downloader.DownloadProgressChanged += (sender, e) =>
                {
                    progress.Tick((int)(e.ProgressPercentage * 100));
                };


                var info = await panApiClient.GetFileInfoAsync(tuple.Item1.fs_id, cancellationSource.Token);
                var url = panApiClient.GetDownloadFileUrl(info);
                string tmpfile;
                if (tuple.Item3==null)
                {
                    tmpfile = $"{tuple.Item2}.{Guid.NewGuid().ToString("N")}.tmp";
                    await downloader.DownloadFileTaskAsync(url, tmpfile, cancellationSource.Token);
                }else
                {
                    tmpfile = tuple.Item3.FileName;
                    tuple.Item3.Address = url;
                    await downloader.DownloadFileTaskAsync(tuple.Item3);
                }

                if (cancellationSource.Token.IsCancellationRequested)
                {
                    if (snapshot==null)
                    {
                        return;
                    }
                    lock (snapshot)
                    {
                        snapshot.Add((tuple.Item1, downloader.Package));
                    }
                }
                else
                {
                    File.Move(tmpfile, tuple.Item2, true);

                    Logger.LogDebug("正在下载 {filename} 到 {localpath}", tuple.Item1.path, tuple.Item2);
                    progress.Tick(10000);
                    completedsize += (ulong)tuple.Item1.size;
                    mainprogressBar.Tick((int)(completedsize * 10000 / totalsize));
                    mainprogressBar.Message = $"{((long)completedsize).Bytes().Humanize()}/{((long)totalsize).Bytes().Humanize()}";
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Fail to download {filename}", tuple.Item1.path);
            }
        }



        internal void AddDownloadFile(BaiduPanFileInfo item, string l)
        {
            //             string[] filesArray = l.Split(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar);
            //             var invalids = System.IO.Path.GetInvalidFileNameChars();
            //             var newName = String.Join("_", l.Split(invalids, StringSplitOptions.None)).TrimEnd('.');

            downloadqueue.Post((item, l,null));

            Interlocked.Add(ref totalsize, (ulong)item.size);
        }

        internal Task Finish()
        {
            downloadqueue.Complete();
            return downloadqueue.Completion;
        }

        List<ValueTuple<BaiduPanFileInfo,DownloadPackage>>? snapshot;
        private bool disposedValue;

        internal async Task<List<ValueTuple<BaiduPanFileInfo, DownloadPackage>>> Pause()
        {
            snapshot = new List<ValueTuple<BaiduPanFileInfo, DownloadPackage>>();
            cancellationSource.Cancel();
            try
            {
                await downloadqueue.Completion;
            }
            catch (OperationCanceledException)
            {
            }

            //ProgbarUpdate.Dispose();
            ConsoleProgress?.Dispose();
            disposedValue = true;

            return snapshot;
        }
        internal void Resume(List<(BaiduPanFileInfo, DownloadPackage)> packages)
        {

            foreach (var package in packages)
            {
                var p = Path.Combine(Path.GetDirectoryName(package.Item2.FileName)??"",
                    Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(package.Item2.FileName)));
                Interlocked.Add(ref totalsize, (ulong) package.Item1.size);
                downloadqueue.Post((package.Item1, p, package.Item2));
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //ProgbarUpdate.Dispose();
                    ConsoleProgress?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~Download()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

    }
}
