# BaiduSync — 百度网盘 → Unity 美术资源同步

把百度网盘里某个文件夹的美术 PNG，同步进 Unity 工程的
`Project-Rule/Assets/_Project/Art/Sprites/`。

底层用 [BaiduPCS-Go](https://github.com/qjfoidnh/BaiduPCS-Go)（已下载到 `bin/`），
`sync_assets.py` 负责登录、拉取、过滤 PNG、落进 Assets 并打印汇总。

```
Tools/BaiduSync/
├── sync_assets.py         主脚本
├── config.example.json    配置模板
├── config.json            你填的配置（含 cookie，已被 .gitignore 排除）
├── bin/BaiduPCS-Go.exe    下载引擎（已被 .gitignore 排除）
└── .staging/              拉取中转区（已被 .gitignore 排除，可随时删）
```

## 一次性准备

**1. 拿 cookie**

浏览器登录百度网盘 → `F12` → `Network` 标签 → 刷新页面 → 点第一条请求 →
右侧 `Headers` → `Request Headers` 里找到 `Cookie:`，从冒号后一直复制到行尾
（**必须含 `BDUSS=`**，带上 `STOKEN=` 的话将来也能用转存功能）。

**2. 填配置**

```bash
cp Tools/BaiduSync/config.example.json Tools/BaiduSync/config.json
```

然后编辑 `config.json`：

| 字段 | 说明 |
| --- | --- |
| `cookies` | 上一步复制的一整段 |
| `remote_dir` | 网盘里的目录，**绝对路径**，如 `/我的资源/规则游戏/美术` |
| `local_dir` | 本地目标，相对仓库根（也可写绝对路径） |
| `png_only` | 默认 `true`，只把 PNG 搬进 Unity（下载阶段就按通配符过滤，psd/mp4 根本不拉）|
| `strip_remote_root` | 默认 `true`，剥掉远端目录路径那几层（见下方说明）|
| `rename_files` | 默认 `false`。开启后每个目录里的帧按自然序重编成 `Hero_<目录名>_NN.png` |
| `rename_map` | 中文目录 → 目标目录的映射，键是「远端相对目录路径」。值写 `null` 表示这个目录不要 |
| `threads` / `parallel_files` | 单文件线程数 / 同时下载文件数 |
| `extra_args` | 透传给 BaiduPCS-Go 的额外参数，一般留空 |

> `strip_remote_root` 的作用：网盘 `/规则游戏/主角/跑步/跑步序列/` 下下来，
> 剥掉 `规则游戏/主角` 这几层后，落进 `newHero/` 的就是 `跑步/跑步序列/...`。

### `rename_map` 的名字从哪来

**目录名 = 动画状态机里的 state 名**，权威定义在
`Scripts/Editor/Animations/HeroAnimatorBuilder.cs` 的 `StateNames` 数组
（那是生成 `Hero.controller` 的声明式真相源）。23 个 state：

```
地面  Idle · Walk · Run
空中  JumpStart · Airborne · Weightless · WallSlide · WallJump
机动  Push · Land
反应  Hurt · Die
战斗  DrawSword · Slash1 · Slash2 · SlashDown · Sheathe · Throw
交互  PullOut · PutBack · OrbHold · PickupOne · PickupBoth
```

其中 `WallSlide` **没有独立素材** —— `HeroAnimatorBuilder.EnsureWallSlideClip()`
是取 `WallJump` 第 3 帧现场生成定格剪辑的，所以映射表里不需要它。

键取**最长匹配前缀**，所以既能整层压平（值只写一层名），也能只替换其中一段。
改完跑 `--install-only` 几秒生效，不用重新下载。
⚠️ 改了名字再重跑，**旧目录名不会自动消失**（脚本不删东西），先删掉目标目录再跑。

### `rename_files` 的编号怎么排

每组内按**自然序**（数字按数值比大小），所以 `蹬墙跳-3.png` 排在 `蹬墙跳-10.png` 前，
产出 `Hero_WallJump_03.png` / `Hero_WallJump_04.png`。用纯字典序会把顺序搞乱。


## 用法

```bash
python Tools/BaiduSync/sync_assets.py
```

先跑一次 `--dry-run` 确认远端路径没写错：

```bash
python Tools/BaiduSync/sync_assets.py --dry-run
```

| 参数 | 作用 |
| --- | --- |
| `--dry-run` | 只测远端目录读不读得到，不下载不落地 |
| `--refresh` | 远端改了图时用，覆盖暂存区的同名文件 |
| `--clean` | 先清空暂存区再拉一遍 |
| `--all-files` | 非 PNG 也搬进 Unity |
| `--remote /别的/路径` | 临时换远端目录，不改配置文件 |

## 它怎么做到「增量」

- **网盘 → 暂存区**：BaiduPCS-Go 自带「自动跳过重名文件」，重复跑只拉新增的。
  远端某个文件改了内容，要加 `--refresh` 才会重下。
- **暂存区 → Unity**：逐个文件比对大小 + SHA-256，只有新增和内容真变了的才落盘，
  所以 Assets 里不会有没意义的改动，也不会平白多出一堆 .meta 变动。

## 落进 Unity 之后

Unity 回到前台会自动导入新图并为每张 PNG 生成 `.meta`。
本项目要求 `*.meta` 必须提交（见根目录 `.gitignore` 末尾的说明），
所以同步完记得 `git status` 确认新图和 `.meta` 都在一起。

> ⚠️ 同步进来的图**不会**自动套用项目要求的导入设置
> （PPU=16 / Pivot=Center / Filter=Point / Compression=None，见
> `Doc/美术与动画接入方案.md`）。需要的话再写一个 Editor 脚本批量设，
> 或者先在 Project 窗口选中这批图统一改一次。

## 出问题了

**登录失败 / cookie 过期**：cookie 有有效期，重拿一次填进 `config.json`。

**下载报 `user is not authorized` 或限速严重**：在 `config.json` 的
`extra_args` 里加 `"--mode=pcs"` 换下载模式（默认是 `locate`）。

**远端路径写错**：用 `--dry-run` 试，它会直接打印远端的报错。

**中文乱码**：脚本对 BaiduPCS-Go 的输出按 UTF-8 → GBK 顺序解码；
Windows 控制台如果不是 UTF-8 代码页，可以先 `chcp 65001`。

## 安全说明

`config.json`（含 cookie）和 `bin/`（14 MB 二进制）都在 `.gitignore` 里，
**不要**把它们提交进仓库。cookie 等同于账号登录态，泄露了别人能直接操作你的网盘。
