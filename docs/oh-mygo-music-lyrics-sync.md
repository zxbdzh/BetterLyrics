# BetterLyrics 与 Oh MyGo Music 歌词联动设计

状态：已实现（2026-11-22）。两端逻辑测试和 BetterLyrics Windows x64 构建通过，尚未做真实桌面双应用验收。

## 目标

用户在 BetterLyrics 手动搜索并保存歌词后，Oh MyGo Music 立即使用同一份歌词。用户切歌、重启任一应用或重新打开 BetterLyrics 后，两边仍按同一个歌曲身份使用这份歌词。

歌词显示样式、歌词窗口布局、滚动位置和播放控制状态不属于同步范围。

## 已确认决策

- BetterLyrics 点击保存后自动同步，搜索预览和切换候选不触发写入。
- Oh MyGo Music 保存本地歌词覆盖记录，不修改音频文件、旁车文件或原始在线歌词。
- 歌曲身份优先使用来源与稳定歌曲 ID。本地歌曲使用规范化路径。没有稳定身份时才使用标题、歌手、专辑和时长组合。
- 同步原文、译文、逐行时间和已有逐字时间。
- 手动覆盖优先。清除覆盖后恢复 Oh MyGo Music 的原始歌词来源。
- 同步失败时，BetterLyrics 仍保存自己的匹配与缓存，并向用户提示失败原因。不创建后台离线写入队列。
- 第一版只允许 BetterLyrics 编辑歌词覆盖，Oh MyGo Music 负责保存、读取和清除覆盖。
- Oh MyGo Music 的歌词写入权限默认关闭，开启后才接受 BetterLyrics 的覆盖写入。

## 已实现的调用链

BetterLyrics 通过 `status`、`lyrics?trackId=...` 和 `subscribe-player-status` 读取 Oh MyGo Music，手动保存时调用 `PUT /lyrics-override`。`trackId` 只在当前播放会话内有效，不能直接作为覆盖记录的长期键。

BetterLyrics 的 `SaveAsync()` 先保存歌曲匹配映射和所选歌词的完整副本，再校验当前歌曲并写入 Oh MyGo Music。预览候选不会写入；同步失败不会撤销本地保存，也不会建立后台重试队列。

Oh MyGo Music 在用户数据目录的 `betterlyrics-lyrics.json` 保存覆盖，加载歌词时先查覆盖，再查原始来源。BetterLyrics 把返回内容转换成 `LyricsCacheItem`，并在解析后恢复来源提供的逐字时间。时间轴在 LiteDB 中以 JSON 保存，避免数据库默认裁剪音节间的空格。

相关代码入口如下。

- `src/BetterLyrics.DotNet/BetterLyrics.Core/ViewModels/LyricsSearchControlViewModel.cs`
- `src/BetterLyrics.DotNet/BetterLyrics.Core/Implementations/Services/GsmtcService/OhMyGoMusicClient.cs`
- `src/BetterLyrics.DotNet/BetterLyrics.Core/Implementations/Services/GsmtcService/GsmtcServiceBase.LyricsUpdater.cs`
- `src/BetterLyrics.DotNet/BetterLyrics.Core/Models/Http/OhMyGoMusicModels.cs`
- `src/BetterLyrics.DotNet/BetterLyrics.Core/Implementations/Services/GsmtcService/OhMyGoMusicLyricsAdapter.cs`

## 用户流程

### 开启联动写入

Oh MyGo Music 在 BetterLyrics 联动设置旁增加独立的歌词写入权限。权限关闭时保持当前只读联动行为，建议默认关闭以兼容已有安装。

BetterLyrics 在当前播放器是 Oh MyGo Music 时执行以下流程。

1. 用户搜索歌词并选中结果。
2. 用户点击保存。
3. BetterLyrics 先保存自己的歌曲匹配和歌词缓存。
4. BetterLyrics 重新读取当前播放状态，取得稳定歌曲键和当前播放轨道 ID。
5. BetterLyrics 把完整歌词数据写入 Oh MyGo Music。
6. Oh MyGo Music 更新当前歌曲的有效歌词。
7. BetterLyrics 触发一次歌词刷新，从 Oh MyGo Music 读取同一份有效歌词。

保存动作必须携带当前播放轨道 ID、应用实例 ID 和稳定歌曲键。Oh MyGo Music 在主线程再次核验身份后写入；任一标识已变化就拒绝本次写入，避免把歌词写到下一首歌或重启后的另一首歌。

### 后续切歌与重开

Oh MyGo Music 每次解析当前歌曲时先查找歌词覆盖，再访问原始歌词来源。覆盖存在时直接返回覆盖内容。

BetterLyrics 重新检测到 Oh MyGo Music 后，仍优先调用 Oh MyGo Music 的当前歌词接口。接口返回覆盖内容后，BetterLyrics 按现有适配器解析歌词，因此切歌、切回和重开都得到相同的内容与时间轴。

已成功同步的歌曲以 MyGo 为准，连接不可用时才使用 BetterLyrics 的本地副本。BetterLyrics 一旦确认 MyGo 已清除覆盖，就持久废弃旧副本；之后断线或重新打开也不会恢复被清除的手动歌词。尚未同步成功的手动选择继续保留在 BetterLyrics，只有再次点击保存才重试写入。

## 歌曲身份

稳定歌曲键只包含来源和稳定 ID。

```json
{
  "source": "local",
  "stableId": "normalized-file-path"
}
```

不同来源使用各自稳定的来源 ID。实际本地来源为 `local`，使用规范化后的绝对路径，Windows 路径忽略大小写。没有稳定 ID 时使用标题、歌手、专辑和时长组合的摘要，并执行大小写、空白和常见标点归一化。运行期 `PlaybackID` 不进入持久键。

元数据组合出现多条候选或缺少必要字段时，不自动覆盖。BetterLyrics 保留本地保存结果并提示用户需要确认歌曲身份。

`trackId` 仍用于当前请求的并发保护，不写入覆盖记录作为唯一身份。

## 歌词覆盖

Oh MyGo Music 新增本地覆盖记录，最小字段如下。

```json
{
  "songKey": {
    "source": "local",
    "stableId": "normalized-file-path"
  },
  "lyrics": {
    "lrc": "[00:01.000]原文",
    "translation": "[00:01.000]译文",
    "lines": [
      {
        "start": 1000,
        "end": 3000,
        "text": "原文",
        "translation": "译文",
        "words": [
          {
            "start": 1000,
            "end": 2000,
            "text": "原"
          }
        ]
      }
    ]
  },
  "updatedAt": "2026-11-22T00:00:00Z"
}
```

逐字时间只同步 BetterLyrics 已经拥有的真实时间。没有逐字时间时保留空数组，不根据逐行时间估算并标记为真实数据。

覆盖记录按稳定歌曲键替换。同一首歌再次保存时更新该覆盖，不产生多份候选。

## 接口约定

### 扩展当前状态接口

`GET /status` 增加稳定歌曲身份和覆盖状态。

```json
{
  "trackId": 12,
  "instanceId": "current-app-instance",
  "songKey": {
    "source": "local",
    "stableId": "normalized-file-path"
  },
  "title": "歌曲标题",
  "artist": "歌手",
  "album": "专辑",
  "durationMs": 183000,
  "lyricsReady": true,
  "lyricsOrigin": "override",
  "lyricsRevision": 3,
  "lyricsWriteEnabled": true
}
```

### 写入覆盖

`PUT /lyrics-override` 接收当前轨道 ID、应用实例 ID、稳定歌曲键和完整歌词数据。

```json
{
  "trackId": 12,
  "instanceId": "current-app-instance",
  "songKey": {
    "source": "local",
    "stableId": "normalized-file-path"
  },
  "lyrics": {
    "lrc": "[00:01.000]原文",
    "translation": "[00:01.000]译文",
    "lines": []
  }
}
```

成功时返回覆盖已经应用。权限关闭、歌曲已切换、身份有歧义或数据校验失败时返回明确错误码，BetterLyrics 根据错误码显示提示。

写入和清除请求要求 `X-BetterLyrics-Client: BetterLyrics`，仅接受回环主机，拒绝带 `Origin` 或浏览器跨站标识的请求。该标识不承担本机应用认证，只阻止网页跨站写入。写入体限制为 2 MiB，非法行时间、词时间或不一致的词文本被拒绝。

### 清除覆盖

`DELETE /lyrics-override?source=...&stableId=...` 按稳定歌曲键清除覆盖。清除后，Oh MyGo Music 重新使用原始歌词来源，并增加歌词修订号，通知 BetterLyrics 刷新同一首歌。

Oh MyGo Music 的播放设置提供“恢复当前歌曲原始歌词”，仅当前歌曲存在覆盖时可用。该本机恢复操作不依赖是否允许外部写入，关闭写入权限不会阻止用户撤销自己的覆盖。

### 读取有效歌词

现有 `GET /lyrics?trackId=...` 继续返回有效歌词。返回内容应包含稳定歌曲键和歌词来源，BetterLyrics 用它确认返回结果与当前歌曲一致。

## 失败与边界

- 写入权限关闭时，BetterLyrics 完成本地保存并提示 Oh MyGo Music 未允许写入。
- Oh MyGo Music 不可访问时，BetterLyrics 完成本地保存并提示同步失败。
- 歌曲在写入前发生切换时，Oh MyGo Music 拒绝写入，BetterLyrics 提示重新选择当前歌曲。
- 身份信息不完整或存在歧义时，不自动覆盖。
- 同一请求重复提交时按稳定歌曲键幂等更新，不生成重复记录。
- Oh MyGo Music 读取覆盖失败时回退到原始歌词来源，并记录可诊断日志。
- 清除覆盖后，BetterLyrics 不应再次自动把旧的手动结果写回 Oh MyGo Music。只有用户再次点击保存才创建覆盖。

## 实现顺序

1. Oh MyGo Music 增加稳定歌曲键、覆盖记录和有效歌词解析顺序。
2. Oh MyGo Music 增加写入权限、写入、清除和覆盖状态接口。
3. BetterLyrics 扩展状态与歌词模型，保留当前 `trackId` 的并发保护。
4. BetterLyrics 在手动保存成功后写入覆盖，并处理权限、离线、切歌和歧义错误。
5. 增加客户端、适配器和端到端联动测试。

## 验收场景

- 开启写入权限后，BetterLyrics 保存手动歌词，Oh MyGo Music 当前歌曲立即显示相同原文、译文和时间轴。
- 切换到另一首歌再切回，覆盖歌词仍然存在。
- 关闭并重新打开两个应用，覆盖歌词仍然存在。
- 关闭写入权限后保存歌词，BetterLyrics 本地结果存在，Oh MyGo Music 原始歌词不被修改，并显示提示。
- Oh MyGo Music 离线时保存歌词，BetterLyrics 本地结果存在，恢复连接后不会自动静默写入旧结果。
- 清除覆盖后，Oh MyGo Music 和 BetterLyrics 都恢复原始歌词来源。
- 模拟写入期间切歌，旧歌曲歌词不会写入新歌曲。
- 同名歌曲或版本差异导致身份歧义时，不发生自动覆盖。

## 决策结论

第一版只允许 BetterLyrics 编辑覆盖，Oh MyGo Music 负责保存、读取和清除覆盖，不提供第二个歌词编辑入口。这样可以保持写入方向单一，避免版本冲突和回写循环。

歌词写入权限默认关闭。用户在 Oh MyGo Music 中明确开启后，BetterLyrics 才能创建或替换歌词覆盖。

## 开启与验收

1. 两端使用包含本次联动实现的版本。
2. 在 Oh MyGo Music 的播放设置开启“与 BetterLyrics 联动”和“允许 BetterLyrics 修改歌词”。
3. 播放歌曲，在 BetterLyrics 手动搜索并点击保存，检查两端原文、译文和时间轴一致。
4. 切歌后切回，再重开应用；随后在 MyGo 恢复原始歌词，检查 BetterLyrics 不再读回旧覆盖。

## 自动验证记录

- BetterLyrics 核心测试：62 项通过，包含保存前先落盘、权限拒绝、歌曲切换、离线回退、清除后离线重开、同步不受显示开关影响，以及逐字时间与空格的持久化。
- BetterLyrics WinUI3：Windows x64 构建通过；未更改现有依赖漏洞和 XAML 绑定警告。
- Oh MyGo Music：`CGO_ENABLED=0 go test ./internal/... -count=1` 全部通过，覆盖跨重启、清除、主线程切歌保护、跨站请求拒绝和磁盘失败回滚。
- 两端 `git diff --check` 通过；未运行真实音频设备或自动操作桌面窗口。
