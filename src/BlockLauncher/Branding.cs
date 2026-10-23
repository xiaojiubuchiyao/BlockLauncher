namespace BlockLauncher
{
    /// <summary>
    /// 换名字、换仓库、换维护者，只改这一个文件。
    /// 任何人 fork 之后改掉这里 + 换成自己的签名公钥，就是一个独立发行版。
    /// </summary>
    public static class Branding
    {
        /// <summary>
        /// 程序名。用于数据目录、exe 文件名、User-Agent、launcher_name 等"落盘和联网"的地方。
        /// 带空格是刻意的（主人指定 Block Launcher），涉及路径拼接的地方一律加引号。
        /// C# 标识符不允许空格，所以命名空间是 BlockLauncher（无空格），两者不是一回事。
        /// </summary>
        public const string AppName = "Block Launcher";

        /// <summary>界面显示名（窗口标题、关于页）。目前和 AppName 一致。</summary>
        public const string DisplayName = "Block Launcher";

        public const string Tagline = "去中心化 Minecraft 启动器";

        /// <summary>
        /// 主页 / 源码下载（仅用于"关于"页面和 User-Agent，不是更新的唯一来源）。
        /// 跟着 RepoOwner / RepoName 走，别单独改这一行，否则会把用户送到别的发行版去。
        /// </summary>
        public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;

        /// <summary>
        /// 代码托管账号与仓库名。所有镜像更新源都由这两个值拼出来，
        /// 换账号 / 换仓库名只改这里，不要逐个改下面的地址。
        /// </summary>
        // 只有这两行需要改，更新源 / RepoUrl / User-Agent 全部由它们拼出来。
        public const string RepoOwner = "xiaojiubuchiyao";

        /// <summary>GitHub 仓库名不能含空格（会被自动转成连字符），所以这里用连写形式。</summary>
        public const string RepoName = "BlockLauncher";

        public const string RepoBranch = "main";

        /// <summary>
        /// 内置的更新清单地址。任意一个能访问到、且签名验证通过就行，
        /// 用户也可以在 设置 → 更新 里自己加镜像。顺序不代表优先级：会全部尝试并取版本最高的有效清单。
        /// 全部指向本仓库（及其镜像），不掺入原作者的服务器 —— 那是另一套发行版的更新通道。
        /// </summary>
        public static readonly string[] DefaultUpdateSources =
        {
            $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{RepoBranch}/release/update.json",
            $"https://cdn.jsdelivr.net/gh/{RepoOwner}/{RepoName}@{RepoBranch}/release/update.json",
            $"https://fastly.jsdelivr.net/gh/{RepoOwner}/{RepoName}@{RepoBranch}/release/update.json",
            $"https://gitee.com/{RepoOwner}/{RepoName}/raw/{RepoBranch}/release/update.json",
            $"https://ghfast.top/https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{RepoBranch}/release/update.json",
        };

        /// <summary>
        /// 内置信任的发布公钥（RSA，.NET XML 格式）。用 tools/Release 的 keygen 生成。
        /// 更新清单必须由其中任意一把私钥签名才会被接受。
        /// 维护者失联时，社区可以发布新公钥，由用户在 设置 → 更新 → 信任的发布者 里手动添加。
        /// 这里只放本发行版自己的钥匙：原作者那把（12c461c3…）已移除，防止他人替本发行版签更新。
        /// </summary>
        public static readonly string[] TrustedKeys =
        {
            // 指纹 8dc61dc90fa1f375（当前维护者的发布钥匙，私钥在 E:\mc启动器\keys\private-key.xml，离线备份）
            "<RSAKeyValue><Modulus>v22mPcPrEz9nMqezBYLkQ/n2kC8zYehDXKa9It3YluatFdobCN8kDHmp6+1zBKZprFxwysCCqju3oZr8cDI4aW+Uk5Qi4RXW4WkWw8FZJ5W2GL2Q+/Wk7Q10zXnsUoaUY0KOA7lDJdU749v07B9hOG9Jv8cP9gNDyx2qh2EuHf6A3296LxTaUBhKPRNJzlOTDFRch/PWAtRmeXyka46ta80igSOq6uoaD2dFnQixH05c3FlOUO/FdSoZe6OCsAsZKIPd3YmiwRyijxlK5cRowfkaITSH3xvTQNi6ZpUAL2cgkqOxoyVX7s8FV4mMA5eXgoHp+0aT4fHfnp4WcGaXVD5YhHytbqkKMVH7kMdkv3KSjH3MFooJt8wUwdjf07NPvNreRTEuKPqiNYsbPKBHRzH9RPL27nLICihIUSvN+Wo4WrEDTqCACEthtlqMmwE53DlMYqtNYtm6OkNsWShe6RWz5KyB+i7L/156hOHJ/eI+Ge+EgINpn9w3SWC5BGmx</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>",
        };

        /// <summary>
        /// 微软正版登录用的 Azure 应用 Client ID。
        /// 需要自己在 Azure 注册应用并向 Mojang 申请 Minecraft API 权限后填入；
        /// 留空时用户可在 设置 → 账户 里填自己的。
        /// </summary>
        public const string DefaultMicrosoftClientId = "8f18bb3f-14fb-410f-85d7-c804dd831630";

        /// <summary>CurseForge API Key（可选）。留空时自动走无需 Key 的镜像。</summary>
        public const string DefaultCurseForgeApiKey = "";
    }
}
