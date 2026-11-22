---
status: accepted
---

# BetterLyrics 写入 Oh MyGo Music 歌词覆盖

BetterLyrics 与 Oh MyGo Music 需要共享用户手动选择的歌词，同时保留 Oh MyGo Music 原始歌词来源。决定由 BetterLyrics 作为第一版唯一的歌词编辑入口，Oh MyGo Music 保存按稳定歌曲键索引的本地歌词覆盖，并在读取时优先返回覆盖。歌词写入权限默认关闭，用户明确开启后才接受 BetterLyrics 的覆盖写入。这样可以保留当前只读联动的兼容路径，避免两个应用互相回写，也避免把用户修改写入音频文件或第三方歌词来源。
