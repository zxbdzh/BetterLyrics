# IKUN 与 LX Music 兼容设计

## 目标

让 `ikun-music-desktop` 直接复用 BetterLyrics 现有 LX Music 接入链路，支持普通歌曲和播客的进度、时长、封面、歌词、逐字稿、转写状态及转写控制。

## 方案

在 `PlayerIdHelper.IsLXMusic` 中增加 IKUN 播放器标识：

- `ikun-music-desktop`
- `ikun-music-desktop.exe`

其余代码继续使用现有 `LXMusicServer` 地址和 SSE/API 协议。IKUN 与 LX Music 共用 `http://127.0.0.1:23330` 默认端口及 `/status`、`/subscribe-player-status` 接口；IKUN 专有的播客接口由已存在的转写客户端继续调用。

当已有配置中的 `LXMusicServer` 为空时，运行时自动回退到上述默认地址；用户填写自定义地址后，当前 LX/IKUN 会话立即重建 SSE 订阅。

## 行为与错误处理

- 检测到 IKUN 后自动启动现有 LX SSE 订阅。
- SSE 提供的精确进度、封面及播客事件覆盖通用 SMTC 的对应信息。
- ASR 仍在 `preparing` 时也拉取增量逐字稿；只要 IKUN 返回部分行，就立即按词级时间轴显示。
- API 未开启或地址错误时，沿用现有连接错误提示；通用 SMTC 播放控制仍可用。
- 设置页继续使用现有 LX Music Server 配置，不增加重复的 IKUN 配置项。

## 验证

- 播放器标识匹配测试覆盖 IKUN 两种标识和原有 LX 标识。
- BetterLyrics Release 构建无错误。
- IKUN 开启本机 API 后，验证 BetterLyrics 能收到 SSE 播放状态，并验证播客逐字稿与转写状态路径仍可用。
