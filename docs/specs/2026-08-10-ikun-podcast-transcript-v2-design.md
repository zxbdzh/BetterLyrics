# IKUN 播客逐字稿 v2 设计

日期：2026-08-10

## 目标

IKUN 负责播客音频、逐字稿与转写的完整生命周期，BetterLyrics 仅订阅并显示 IKUN 已发布的逐字稿。中文、英文和中英混合内容均支持逐字高亮；没有可靠词级时间时必须降级为整行显示，不推测时间。

发布 revision 后，BetterLyrics 应在 1 秒内显示新增或更新的行。存在词级时间时，逐字高亮与播放位置的误差目标不超过 300 ms。

## 职责边界

### IKUN

- 管理播客订阅、播放、下载目录和音频缓存目录。
- 选择发布者逐字稿或 ASR 逐字稿，对外只发布统一结果。
- 管理模型检查、下载、ASR、重试、重新生成和持久化。
- 只在用户手动发起后生成 ASR，不默认自动转写。
- 将音频按约 30 秒切片，并保留少量上下文重叠。
- 使用全局统一 ASR 队列。当前切片不中断；播放位置改变后，从下一任务开始优先处理当前位置切片及后续两个切片，再补齐前文和其余内容。
- 每完成一个切片即发布一次 revision，允许使用稳定行 ID 更新已有识别结果。
- 为最终 `displayText` 直接提供字符索引和词级时间。

### BetterLyrics

- 从现有 LX/IKUN 媒体会话获取 `mediaKind`、`contentId` 和逐字稿 descriptor。
- 播客会话调用 `/transcript` 拉取 revision 增量并保存在进程内存中。
- 将逐字稿映射为现有 `CurrentLyricsData`，复用歌词页面、逐字高亮、点击跳转和花再音响链路。
- 不下载音频，不缓存逐字稿，不运行或管理模型，不执行 ASR，不分词，不翻译，不纠错，不做拼音、罗马音或繁简转换。
- 不展示模型状态、转写进度、转写明细或转写控制按钮。
- 不调用 IKUN 的 `/transcription-status` 和 `/transcription-control`。

## 会话发现

IKUN 通过现有 SSE 会话发布以下事件：

- `progress`
- `duration`
- `picUrl`
- `mediaKind`
- `contentId`
- `transcript`

`transcript` 事件的数据是 `TranscriptDescriptor`。不再发布或消费 `transcription` 事件。

当播放器命中 LX/IKUN 兼容会话时，BetterLyrics 最多等待 1 秒以确认 `mediaKind`：

- `mediaKind=podcast`：进入播客展示模式，禁止普通歌词搜索。
- `mediaKind=music`：按现有音乐流程处理。
- 旧 LX 在 1 秒内未发布 `mediaKind`：按普通音乐处理。

切换 `contentId` 或切换到非播客媒体时清空当前逐字稿。SSE 短暂断开时保留当前内存字幕，并在后台退避重连。

## v2 数据模型

所有 v2 消息都必须包含 `protocolVersion: 2`。BetterLyrics 不猜测或兼容 v1 响应。

### TranscriptDescriptor

```json
{
  "protocolVersion": 2,
  "contentId": "episode-id",
  "revision": 12,
  "state": "preparing",
  "isPartial": true
}
```

字段：

- `contentId`：当前播客单集的稳定标识。
- `revision`：该单集逐字稿的单调递增版本。
- `state`：`missing`、`preparing`、`ready`、`failed` 或 `unavailable`。
- `isPartial`：当前是否仅发布了部分内容。

来源选择属于 IKUN 内部实现，v2 展示协议不要求 BetterLyrics 根据来源分支。

### TranscriptDelta

请求：

```text
GET /transcript?contentId={contentId}&sinceRevision={revision}
```

响应：

```json
{
  "protocolVersion": 2,
  "contentId": "episode-id",
  "baseRevision": 11,
  "revision": 12,
  "reset": false,
  "state": "preparing",
  "isPartial": true,
  "upsertLines": [],
  "deletedLineIds": [],
  "speakers": []
}
```

- 正常增量的 `baseRevision` 必须等于请求方的 `sinceRevision`。
- `upsertLines` 使用稳定行 ID 新增或替换行。
- `deletedLineIds` 删除指定行。
- `reset=true` 表示响应是完整快照，接收方必须原子替换当前内存内容。
- 服务端无法连续提供请求 revision 后的增量时，返回 `reset=true` 的完整快照。
- BetterLyrics 检测到 revision 断档时清空内存，并以 `sinceRevision=0` 重新请求。
- 数据不合法时，BetterLyrics 保留最后一次有效字幕，仅记录日志。

### TranscriptLine

```json
{
  "id": "episode-id:segment-17:line-2",
  "startMs": 514230,
  "endMs": 517880,
  "displayText": "今天我们聊 AI agents。",
  "speakerId": "host",
  "words": [
    {
      "id": "episode-id:segment-17:line-2:word-0",
      "startIndex": 0,
      "length": 1,
      "startMs": 514230,
      "endMs": 514510
    }
  ]
}
```

- `displayText` 是唯一展示文本。
- `startIndex` 和 `length` 是基于最终 `displayText` 的 UTF-16 字符索引，与 .NET 和 JavaScript 字符串索引一致。
- 每个词的范围必须落在 `displayText` 内；词时间必须有限、递增并落在行时间范围内。
- 词可以覆盖汉字、英文单词、数字或中英混合片段，未覆盖的空格和标点保持静态显示。
- `words` 为空表示没有可靠词级时间，BetterLyrics 整行显示，不创建伪造音节。
- `speakerId` 可选。说话人名称由 `speakers` 解析，并以静态 `说话人：` 前缀展示；前缀不参与逐字高亮。

### Speaker

```json
{
  "id": "host",
  "name": "主持人"
}
```

BetterLyrics 仅消费 `id` 和 `name`，不根据说话人来源改变显示行为。

## 状态显示

BetterLyrics 对播客状态使用现有歌词数据通道发布静态行，花再音响收到相同内容：

| 条件 | 显示文本 |
| --- | --- |
| API 不可用或尚未连接 | 等待字幕连接 |
| `preparing` 且没有任何行 | 正在生成字幕 |
| `preparing` 且当前播放切片尚无行 | 当前片段正在生成 |
| `missing` | 暂无字幕，请在 IKUN 中生成 |
| `failed` | 字幕生成失败，请在 IKUN 中重试 |
| `unavailable` | 当前内容不支持字幕 |

`preparing` 可以携带部分行；已有行立即显示。静态状态不触发错误通知。

## IKUN 分段与调度

- 默认切片目标长度为 30 秒，边界可根据音频和识别器能力微调。
- 相邻切片保留少量重叠，合并时按时间、文本和稳定 ID 去重。
- 播放当前位置所在切片为优先级 0，之后两个切片依次为优先级 1 和 2。
- 其余后文优先于已错过的前文；当前位置改变时重新计算尚未开始任务的优先级。
- 已开始识别的切片不中断，避免丢弃昂贵工作。
- 任一时刻全应用只运行一个 ASR 任务；下载、模型准备和音频转换也通过同一任务状态对用户可见。
- 每个切片成功后持久化结果并发布 `preparing + isPartial=true`；全部完成后发布 `ready + isPartial=false`。
- 失败保留已完成切片，状态发布为 `failed`，用户可在 IKUN 中重试缺失切片或重新生成全部内容。

## 容错与重连

- BetterLyrics 对连接失败使用有上限的指数退避，不弹出歌词获取失败通知。
- 短暂断线不清除字幕；重连后从当前 revision 继续拉取。
- descriptor revision 小于本地 revision、base revision 不匹配、非法索引或非法时间均视为协议错误。
- 协议错误不覆盖当前有效字幕，并触发一次从 revision 0 的恢复请求；恢复仍失败则继续保留旧字幕并等待下一次 descriptor。
- 切换单集时，旧单集字幕不得继续显示。

## 验收测试

IKUN 自动测试至少覆盖：

- 中文、英文、中英混合文本的 UTF-16 `startIndex` 和 `length`。
- 无词级时间时 `words=[]`，不按字符平均伪造时间。
- 30 秒切片、当前位置及后续两片优先、跳转后下一任务重排、当前任务不中断。
- 部分结果逐 revision 发布、稳定行 ID upsert、完成/失败/重试。
- `sinceRevision` 连续增量、断档完整 reset。

BetterLyrics 自动测试至少覆盖：

- v2 descriptor 和 delta 校验。
- reset、upsert、delete、revision 断档重拉。
- 说话人静态前缀及基于服务端索引的中文、英文、中英混合逐字高亮。
- 空 words 整行显示。
- `preparing` 部分行和各静态状态文案。
- 断线保留、切换 `contentId` 清空、播客不触发普通歌词搜索。

端到端验证使用真实中文播客，并同时检查 BetterLyrics 窗口和花再音响的当前文本及逐字进度。
