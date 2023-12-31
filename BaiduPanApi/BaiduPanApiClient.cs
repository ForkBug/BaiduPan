﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Web;

namespace Yab.BaiduPanApi
{
    public class BaiduException : Exception
    {
        public BaiduException(long errno, string msg) : base(msg)
        {
            HResult = (int)errno;
        }
    }
    public record BaiduPanFileInfo
    {
        public string fs_id;//baidu said the type is ulong. But it's meaningless for clients.
        public string server_filename;
        public long server_atime;
        public long server_ctime;
        public long local_mtime;
        public string path;
        public bool isdir;
        public long size;
        public string dlink;
    }

    /// <summary>
    /// 对百度盘web api的封装
    /// 百度官方api的文档地址https://pan.baidu.com/union/doc/nksg0sbfs
    /// </summary>
    public class BaiduPanApiClient
    {
        string AccessToken;
        HttpClient client;
        string HostName = "pan.baidu.com";
        ILogger<BaiduPanApiClient> logger;
        int ParallDegree;

        public BaiduPanApiClient(string accessToken, HttpClient client, ILogger<BaiduPanApiClient> logger, int parallDegree)
        {
            AccessToken = accessToken;
            this.client = client;
            this.logger = logger;
            ParallDegree = parallDegree;
        }


        /// <summary>
        /// with retry on failure
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        protected async Task<HttpResponseMessage> GetResponse(string url, CancellationToken cancellationToken)
        {
            Exception e = null;
            HttpResponseMessage ret = null;
            for (int i = 0; i < 3; ++i)
            {
                try
                {
                    HttpRequestMessage msg=new HttpRequestMessage(HttpMethod.Get, url);
                    var resp =  await client.SendAsync(msg, cancellationToken);
                    if (resp.IsSuccessStatusCode)
                    {
                        return resp;
                    }
                    if (resp.StatusCode==System.Net.HttpStatusCode.Unauthorized)
                    {
                        return resp;
                    }
                    logger.LogTrace("{RequestUri} return {StatusCode}", url, resp.StatusCode);
                    ret = resp;
                }
                catch(TaskCanceledException)
                {
                    throw e;
                }
                catch (Exception ex)
                {
                    logger.LogTrace("Exception occurs when sending {RequestUri}, message {Message}", url, ex.Message);
                    e = ex;
                }
                if (i!=2)
                {
                    await Task.Delay(1500 * (i + 1));
                }
            }
            if (e!=null)
            {
                logger.LogError("Exception occurs when sending {RequestUri}, message {Message}", url, e.Message);
                throw e;
            }
            return ret;
        }


        private string BuildApiUrl(string hostName, string apiv, List<(string, string)> parms)
        {
            var strb = new StringBuilder($"https://{hostName}{apiv}&access_token={AccessToken}");
            foreach (var item in parms)
            {
                strb.Append("&");
                strb.Append(item.Item1);
                strb.Append("=");
                strb.Append(HttpUtility.UrlEncode(item.Item2));
            }
            strb.Append("&");
            strb.Append("openapi");
            strb.Append("=");
            strb.Append(HttpUtility.UrlEncode("xpansdk"));
            return strb.ToString();
        }


        /// <summary>
        /// listall接口的请求频率建议不超过每分钟8-10次
        /// </summary>
        /// <param name="path"></param>
        /// <param name="re"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        /// <exception cref="BaiduException"></exception>
        public async IAsyncEnumerable<BaiduPanFileInfo> ListAllFilesAsync(string path, Regex re,
            [EnumeratorCancellation] CancellationToken cancellationToken, int limit = 10000)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }
            long start = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parms = new List<ValueTuple<string, string>>()
                {
                    ( "start",start.ToString() ),
                    ("recursion","1"),
                    ("limit",limit.ToString()),
                    ("path",path)
                };
                var strpath = BuildApiUrl(HostName, "/rest/2.0/xpan/multimedia?method=listall", parms);

                var resp = await GetResponse(strpath, cancellationToken);
                var deley =  Task.Delay(8000);//百度有流控
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogError("获取文件/夹信息失败{0},错误信息{1}，可以稍后重试", path, resp.StatusCode);
                    yield break;
                }
                var str = await resp.Content.ReadAsStringAsync(cancellationToken);
                JObject obj = JObject.Parse(str);
                int err = obj.Value<int>("errno");
                if (err != 0)
                {
                    var errmsg = err switch
                    {
                        42213 => "没有共享目录的权限",
                        31066 => "文件不存在",
                        31034 => "命中频控,listall接口的请求频率建议不超过每分钟8-10次",
                        _ => "未知错误",
                    };
                    throw new BaiduException(err, errmsg);
                }
                foreach (var i in obj["list"])
                {
                    var item = i.ToObject<BaiduPanFileInfo>();
                    if ((re != null) && (re.IsMatch(item.path)))
                    {
                        logger.LogDebug("Filter out {file}", item.path);
                        continue;
                    }
                    yield return item;
                }
                long has_more = obj.Value<long>("has_more");
                if (has_more == 0)
                {
                    yield break;
                }
                start = obj.Value<long>("cursor");
                await deley;
            }

        }

        public async ValueTask<BaiduPanFileInfo> GetFileInfoAsync(string fsids, CancellationToken cancellationToken)
        {
            return (await GetFileInfoAsync(new string[] { fsids }, cancellationToken)).FirstOrDefault();
        }
        public async ValueTask<List<BaiduPanFileInfo>> GetFileInfoAsync(string[] fsids,CancellationToken cancellationToken)
        {
            if (fsids.Length==0 || (fsids.Length>100))
            {
                throw new InvalidDataException("0< fsids.Length <100");
            }
            var parms = new List<ValueTuple<string, string>>()
            {
                ( "fsids",$"[{string.Join(',',fsids)}]" ),
                ("dlink","1")

            };
            var strpath = BuildApiUrl(HostName, "/rest/2.0/xpan/multimedia?method=filemetas", parms);


            var resp = await GetResponse(strpath, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine("获取文件/夹信息失败{path},错误信息{error}，可以稍后重试", string.Join(',', fsids), resp.StatusCode);
                return new List<BaiduPanFileInfo>();
            }
            var str = await resp.Content.ReadAsStringAsync();
            JObject obj = JObject.Parse(str);
            int err = obj.Value<int>("errno");
            if (err != 0)
            {
                var errmsg = err switch
                {
                    42213 => "没有共享目录的权限",
                    42212 => "共享目录文件上传者信息查询失败，可重试",
                    42211 => "图片详细信息查询失败",
                    42214 => "文件基础信息查询失败",
                    _ => "未知错误",
                };
                throw new BaiduException(err, errmsg);
            }
            return obj["list"].Select(x => x.ToObject<BaiduPanFileInfo>()).ToList();
        }


        public string GetDownloadFileUrl(BaiduPanFileInfo baiduPanFileInfo)
        {
            if(string.IsNullOrWhiteSpace(baiduPanFileInfo.dlink))
            {
                throw new InvalidDataException($"{baiduPanFileInfo.path} baiduPanFileInfo.dlink is null.");
            }
            return  $"{baiduPanFileInfo.dlink}&access_token={AccessToken}";
        }


        public async ValueTask<List<BaiduPanFileInfo>> ListFilesAsync(string path, CancellationToken cancellationToken)
        {
            List<BaiduPanFileInfo> lis = new();
            long start = 0;
            int limit = 1000;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parms = new List<ValueTuple<string, string>>()
                {
                    ( "start",start.ToString() ),
                    ( "limit",limit.ToString() ),
                    ( "showempty","1" ),

                };
                if (!string.IsNullOrWhiteSpace(path))
                {
                    parms.Add(("dir", path));
                }
                var strpath = BuildApiUrl(HostName, "/rest/2.0/xpan/file?method=list", parms);


                var resp = await GetResponse(strpath, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine("获取文件/夹信息失败{path},错误信息{error}，可以稍后重试", path, resp.StatusCode);
                    return lis;
                }
                var str = await resp.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(str);
                int err = obj.Value<int>("errno");
                if (err != 0)
                {
                    var errmsg = err switch
                    {
                        -7 => "文件或目录无权访问",
                        -9 => "文件或目录不存在",
                        _ => "未知错误",
                    };
                    throw new BaiduException(err, errmsg);
                }

                var listmp = obj["list"].Select(x => x.ToObject<BaiduPanFileInfo>()).ToList();
                if (listmp.Count != 0)
                {
                    lis.AddRange(listmp);
                }
                if (listmp.Count<limit)
                {
                    break;
                }else
                {
                    start += listmp.Count;
                }
            }
            return lis;
        }

        private class LongRefHelp
        {
            public long Num;
        }
        async IAsyncEnumerable<BaiduPanFileInfo> ProcessOneBaiduPanFileInfo(BaiduPanFileInfo info,
            TransformManyBlock<BaiduPanFileInfo, BaiduPanFileInfo> manyBlock,
            LongRefHelp foldercount,Regex re, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return info;

            long count = 0;
            if (info.isdir)
            {
                var ret = await ListFilesAsync(info.path, cancellationToken);
                foreach (var item in ret)
                {
                    if ((re != null) && (re.IsMatch(item.path)))
                    {
                        logger.LogDebug("Filter out {file}", item.path);
                        continue;
                    }

                    if (item.isdir)
                    {
                        if (!item.path.EndsWith('/'))
                        {
                            item.path += '/';
                        }
                        manyBlock.Post(item);
                        ++count;
                    }
                    yield return item;
                }
            }
            bool closeblock = false;
            lock (manyBlock)
            {
                foldercount.Num += count - 1;
                if (foldercount.Num == 0)
                {
                    closeblock = true;
                }
            }
            if (closeblock)
            {
                manyBlock.Complete();
            }
        }

        public async IAsyncEnumerable<BaiduPanFileInfo> ListAllFilesRecursivelyAsync(string path, System.Text.RegularExpressions.Regex re,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {

            var lis = await ListFilesAsync(path, cancellationToken);
            if (lis.Count > 0)
            {
                TransformManyBlock<BaiduPanFileInfo, BaiduPanFileInfo> refe = null;
                var foldercount = new LongRefHelp { Num = 0 };
                var trans = new TransformManyBlock<BaiduPanFileInfo, BaiduPanFileInfo>(b => ProcessOneBaiduPanFileInfo(b, refe, foldercount,re, cancellationToken),
                    new ExecutionDataflowBlockOptions { EnsureOrdered = false, MaxDegreeOfParallelism = ParallDegree });
                refe = trans;
                bool f = false;
                foreach (var item in lis)
                {
                    if ((re!=null)&&(re.IsMatch(item.path)))
                    {
                        logger.LogDebug("Filter out {file}", item.path);
                        continue;
                    }
                    f = true;
                    lock (trans)
                    {
                        foldercount.Num += 1;
                    }
                    trans.Post(item);
                }
                if (f)
                {
                    var ret = trans.ReceiveAllAsync(cancellationToken);
                    await foreach (var item in ret.WithCancellation(cancellationToken))
                    {
                        if ((re != null) && (re.IsMatch(item.path)))
                        {
                            logger.LogDebug("Filter out {file}", item.path);
                            continue;
                        }
                        yield return item;
                    }
                }
            }

        }
    }
}
