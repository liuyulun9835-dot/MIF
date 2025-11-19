# externals 目录指南

将 ATAS 提供的闭源 DLL 统一放置在本目录下的分组子目录中，例如：

```
externals/
  ATAS/
    ATAS.Indicators.dll
    Utils.Common.dll
```

所有 DOM/Cluster 项目的 `<HintPath>` 均以仓库根目录为基准，例如：

```xml
<HintPath>..\..\externals\ATAS\ATAS.Indicators.dll</HintPath>
```

请确保本地开发与 CI 环境中均同步该目录结构：
1. 对于 CI，可在准备阶段下载或缓存 DLL；
2. 若有未开源组件，使用安全的私有存储（如 Azure DevOps Secure Files、GitHub Encrypted Secrets + Release Pipeline）。

> 若将来引入更多外部依赖，请以供应商为单位创建子目录，并在相应 `.csproj` 中使用相对路径明确引用。
