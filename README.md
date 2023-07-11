# Yab(Yet Another Baidupan console tool)百度盘命令行工具，和百度盘C# SDK
Yab是一款基于百度盘官方Api的高速命令行下载工具，支持Windows，Linux和Macos

Yab特性均基于百度盘官方Api，并不支持破解限速


图图！

有的朋友在开启百度盘会员之后，用官方GUI客户端可以满速，有的朋友却不能满速。我这里测试结果是20MB/s变成50MB/s，打满带宽

## 特性列表
+ 较低的资源占用率
+ 较高的下载速度
+ 列出服务器某文件夹下所有内容
+ 对比服务器和本地文件夹的不同
+ 仅下载本地没有下载的文件/夹
+ 并行下载
+ 每个文件分片下载
+ 支持正则表达式过滤文件名
+ 暂停/继续下载

## 使用方法
### 1. 登录，运行命令行
> yab login

执行之后会使用浏览器打开百度网站登录，成功的话命令行会显示“百度盘登录成功”

如果yab执行环境没有浏览器或GUI环境，可以在有条件的机器上执行yab，成功之后拷贝appsettings.json到目标机器上

login是为了获取Access token存入appsettings.json文件。百度盘文档说Access token 30天过期一次。


### 2. 下载
下载服务器/math到J:\video\math目录，过滤掉所有文件/夹名字含有“过滤”的文件/夹
> yab download -l J:\video\math -r 过滤 -p /math

### 3. 杂项
对比服务器和本地文件夹
> yab diff -p /math  -l J:\video\math -f J:\video\list.txt -r 过滤 

列出服务器文件夹内容
> yab list

> yab list -p /math -n -f J:\video\list.txt -r 过滤 

## 诊断
yab会输出日志到Logs/Log.txt，日志级别可通过编辑appsettings.json更改
配置
## 开发指南
用Visual Studio/Code打开根目录的BaiduPan.sln文件进行开发
+ BaiduPanApi为百度盘C# SDK
+ BaiduPanConsole为命令行工具yab的项目文件

内容都很简单，需要什么功能自己顺手加上即可。

本repo欢迎提交新更改

## License
GPL
