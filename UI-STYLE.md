# ClipVault UI 设计文档

> 本文档详细描述当前 ClipVault 剪贴板管理工具的实际界面布局和样式参数。
> 修改本文档后交给 AI，AI 会根据你的修改来调整代码。

---

## 目录

1. [颜色主题系统](#1-颜色主题系统)
2. [通用控件样式](#2-通用控件样式)
3. [弹出面板整体布局](#3-弹出面板整体布局)
4. [顶部搜索栏与标签栏](#4-顶部搜索栏与标签栏)
5. [卡片列表横向滚动布局](#5-卡片列表横向滚动布局)
6. [剪贴板卡片内部布局](#6-剪贴板卡片内部布局)
7. [右键上下文菜单](#7-右键上下文菜单)
8. [交互动效与行为](#8-交互动效与行为)

---

## 1. 颜色主题系统

**文件**: `Assets/Styles/Colors.xaml` | **风格**: 深色主题（靛蓝紫主色）

### 主色调

| 名称 | 色值 | 用途 |
|------|------|------|
| PrimaryColor | `#4C6EF5` | 主色（靛蓝紫，目前未直接使用） |
| AccentColor | `#5B8DEF` | 强调色（"全部"选中态、标签选中、新建分组文字） |
| AccentGlowColor | `#7C8FF0` | 强调光晕色（透明度15%，预留） |

### 背景层次（由深到浅）

| 名称 | 色值 | 用途 |
|------|------|------|
| BackgroundColor | `#14151A` | 面板最外层背景（最深） |
| SurfaceColor | `#1C1D24` | 搜索框 / 标签chip / 右键菜单背景 |
| CardBackgroundColor | `#222329` | 卡片默认背景 |
| CardHoverColor | `#2C2D36` | 卡片悬停背景 |
| PinnedColor | `#E8A838` | 置顶角标颜色（金色） |

### 前景色

| 名称 | 色值 | 用途 |
|------|------|------|
| PrimaryTextColor | `#F0F1F5` | 主文字颜色（卡片文本、类型图标） |
| SecondaryTextColor | `#8B8D98` | 次要文字（时间戳、搜索图标、清空按钮） |
| BorderColor | `#33343D` | 默认边框颜色 |
| BorderHoverColor | `#45474F` | 悬停边框颜色 |

---

## 2. 通用控件样式

**文件**: `Assets/Styles/Controls.xaml`

### 圆角常量

| 名称 | 值 | 用于 |
|------|------|------|
| CardCornerRadius | `10` | 卡片圆角 |
| SmallCornerRadius | `6` | 按钮 / chip / 搜索框等小元素圆角 |

### 卡片样式 (`ClipboardCard` — Border 容器)

| 属性 | 值 | 说明 |
|------|------|------|
| Background | `#222329` | 默认背景 |
| 悬停 Background | `#2C2D36` | 鼠标悬停背景 |
| CornerRadius | `10` | 圆角 |
| Padding | `14,12` | 内边距（左右14，上下12） |
| BorderThickness | `1` | 边框粗细 |
| BorderBrush | `#33343D` | 默认边框 |
| 悬停 BorderBrush | `#45474F` | 悬停时边框变亮 |

### 卡片操作按钮样式 (`CardActionButton`)

| 属性 | 值 | 说明 |
|------|------|------|
| Background | `Transparent` | 默认透明 |
| 悬停 Background | `#45474F` | 悬停背景 |
| 悬停 Foreground | `#F0F1F5` | 悬停时文字变亮 |
| 默认 Foreground | `#8B8D98` | 默认灰色图标 |
| Padding | `7,5` | 内边距 |
| FontSize | `14` | 字号 |
| CornerRadius | `6` | 圆角 |

### 深色右键菜单样式 (`DarkContextMenuStyle`)

| 属性 | 值 |
|------|------|
| Background | `#1C1D24` (Surface) |
| BorderBrush | `#33343D` |
| BorderThickness | `1` |
| CornerRadius | `8` |
| 阴影 BlurRadius | `16` |
| 阴影 ShadowDepth | `3` |
| 阴影 Opacity | `0.5` |

### MenuItem 样式（全局）

| 属性 | 值 |
|------|------|
| Background | `Transparent` |
| 悬停 Background | `#45474F` |
| Foreground | `#F0F1F5` |
| Padding | `12,7` |
| FontSize | `13` |
| CornerRadius | `5` |
| 子菜单箭头 | `▸` (10px, SecondaryText) |

### Separator 样式

| 属性 | 值 |
|------|------|
| Background | `#33343D` |
| Height | `1` |
| Margin | `8,3` |

---

## 3. 弹出面板整体布局

**文件**: `Views/PopupWindow.xaml`

### 窗口结构图

```
┌──────────────────────────────────────────────────────────────────┐
│  Margin=8（外层透明间距 + 阴影空间）                              │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Background=#14151A  CornerRadius=14  BorderThickness=1   │  │
│  │  DropShadow: BlurRadius=30, ShadowDepth=2, Opacity=0.6    │  │
│  │                                                            │  │
│  │  ┌──────────────────────────────────────────────────────┐ │  │
│  │  │ Row 0 (Auto): 顶部栏 [搜索框 | 标签栏 | 清空]        │ │  │
│  │  │ Margin=18,16,18,12                                    │ │  │
│  │  ├──────────────────────────────────────────────────────┤ │  │
│  │  │ Row 1 (*):    横向滚动卡片列表                        │ │  │
│  │  │                （Mac Paste 风格从左到右）              │ │  │
│  │  └──────────────────────────────────────────────────────┘ │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

### 窗口属性

| 属性 | 值 | 说明 |
|------|------|------|
| WindowStyle | `None` | 无边框 |
| AllowsTransparency | `True` | 透明背景（支持圆角+阴影） |
| Background | `Transparent` | 窗口背景透明 |
| Topmost | `True` | 始终置顶 |
| ShowInTaskbar | `False` | 不显示在任务栏 |
| ResizeMode | `NoResize` | 不可调整大小 |
| ShowActivated | `True` | 显示时获取焦点 |
| Height | `380` | 固定高度（px） |
| Width | 动态 | 代码设置，适配当前显示器全屏宽度 |

### 多显示器定位

| 策略 | 说明 |
|------|------|
| 优先 | 鼠标光标所在显示器（`GetCursorPos` + `MonitorFromPoint`） |
| 回退 | 前台窗口所在显示器（`GetForegroundWindow` + `MonitorFromWindow`） |
| 定位 | 底部贴边，水平居中（`PositionAtBottom`） |
| DPI | 自动转换物理像素 → WPF DIP（`TransformToDevice`） |

### 显示/隐藏动画

| 操作 | 动画 | 时长 |
|------|------|------|
| 显示 | Opacity 0→1 淡入 + 向上位移 | 150ms |
| 隐藏 | Opacity 1→0 淡出 + 向下位移 | 150ms |
| 动画完成后 | 清除动画引用（`BeginAnimation(null)`），防止 HoldEnd 锁定 | — |

---

## 4. 顶部搜索栏与标签栏

### 布局结构（同一行，3列）

```
┌──────────────┬─────────────────────────────────────────────┬──────┐
│ Column 0     │ Column 1 (*, 自动伸缩)                       │ Col 2│
│              │                                              │      │
│ ┌──────────┐ │ ┌──────┐ ┌────┐ ┌────┐ ┌────┐               │      │
│ │🔍 搜索...│ │ │ 全部 │ │ 2  │ │ 3  │ │ 4✕ │  (横向滚动)   │ 清空 │
│ │          │ │ └──────┘ └────┘ └────┘ └────┘               │      │
│ └──────────┘ │                                              │      │
│ Width=280    │ ScrollViewer (横向滚动)                       │      │
│ Margin右=14  │                                              │      │
└──────────────┴─────────────────────────────────────────────┴──────┘
```

### 搜索框（Column 0）

| 属性 | 值 |
|------|------|
| Width | `280` (固定) |
| Background | `#1C1D24` (Surface) |
| CornerRadius | `8` |
| BorderThickness | `1` |
| BorderBrush | `#33343D` → 悬停 `#45474F` |
| Margin（右侧） | `14` |
| 搜索图标 | `🔍` FontSize=15, Margin左14右8, Opacity=0.5 |
| 输入框 FontSize | `15` |
| 输入框 Padding | `0,9,12,9` |
| 输入框 Foreground | `#F0F1F5` |
| 绑定 | `SearchText, UpdateSourceTrigger=PropertyChanged` |

### "全部" Chip（Column 1 行首）

| 属性 | 值 |
|------|------|
| Padding | `12,6` |
| CornerRadius | `6` |
| Margin（右侧） | `6` |
| 选中态 Background | `AccentBrush` (#5B8DEF) |
| 选中态 BorderBrush | `AccentBrush` |
| 选中态文字 | `White`, FontWeight=`Medium`, FontSize=`13` |
| 未选中态 Background | `SurfaceBrush` (#1C1D24) |
| 未选中态 BorderBrush | `BorderBrush` (#33343D) |
| 交互 | 鼠标左键点击切换 |

### 标签 Chip 列表（Column 1，ItemsControl 动态绑定 `Tags`）

```
每个标签 Chip:
┌──────────┐
│ 标签名  ✕ │  ← 左键点击：筛选该分组 | ✕ 左键点击：删除分组
└──────────┘
```

| 属性 | 值 |
|------|------|
| Margin（右侧间距） | `6` |
| Padding | `12,6` |
| CornerRadius | `6` |
| Background | `#1C1D24` (Surface) |
| BorderThickness | `1` |
| BorderBrush | `#33343D` |
| 标签名 FontSize | `13` |
| 标签名 Foreground | `#F0F1F5` |
| 删除 ✕ FontSize | `11` |
| 删除 ✕ Foreground | `#8B8D98` |
| 删除 ✕ Opacity | `0.5` |
| 选中态 | 代码中切换 Background=`AccentBrush`, Border=`AccentBrush` |

### 清空按钮（Column 2）

| 属性 | 值 |
|------|------|
| Content | `清空` |
| FontSize | `14` |
| Foreground | `#8B8D98` (Secondary) |
| Background | `Transparent` |
| BorderThickness | `0` |
| Padding | `14,0` |
| Command | `ClearAllCommand` |

---

## 5. 卡片列表横向滚动布局

**文件**: `Views/PopupWindow.xaml` (Row 1)

### 容器结构

```
ScrollViewer (横向滚动)
  └─ ItemsControl
       └─ ItemsPanel: StackPanel Orientation=Horizontal
            └─ [ClipboardItemCard] [ClipboardItemCard] [ClipboardItemCard] ...
```

### 滚动区域属性

| 属性 | 值 |
|------|------|
| HorizontalScrollBarVisibility | `Hidden` |
| VerticalScrollBarVisibility | `Disabled` |
| Margin | `0,0,0,0` |
| Padding | `10,0,10,12` |
| 鼠标滚轮 | 垂直滚轮→横向滚动（`PreviewMouseWheel` 转换） |
| 滚轮步长 | 约1个卡片宽度 |

### 空状态提示（无记录时显示）

| 属性 | 值 |
|------|------|
| 图标 | `📋` FontSize=52, Opacity=0.15 |
| 文字 | `暂无剪贴板记录` |
| 文字 FontSize | `15` |
| 文字 Foreground | `#8B8D98` |
| 文字 Margin-top | `10` |
| 位置 | 居中对齐 |
| 可见性绑定 | `EmptyStateVisibility` (FilteredItems.Count==0 时显示) |

---

## 6. 剪贴板卡片内部布局

**文件**: `Views/ClipboardItemCard.xaml`

### 卡片整体

```
Width=250px, Margin=5,0
┌─────────────────────────────┐
│  Row 0 (Auto): 顶部行       │
│  ┌────────────┬───────────┐ │
│  │ 📝 🔵Pin   │   📌  🗑   │ │  ← 类型图标+Pin角标 | 悬停操作按钮
│  └────────────┴───────────┘ │
│                             │
│  Row 1 (*): 内容预览区      │
│  ┌─────────────────────────┐│
│  │                         ││
│  │   文本内容 / 图片预览    ││  ← 有图片时显示图片，否则显示文本
│  │                         ││
│  └─────────────────────────┘│
│                             │
│  Row 2 (Auto): 底部行       │
│  ┌─────────────────────────┐│
│  │ 12:30  [标签名]         ││  ← 时间戳 + 分组标签
│  └─────────────────────────┘│
└─────────────────────────────┘
```

| 属性 | 值 |
|------|------|
| Width | `250` (固定) |
| Margin | `5,0` (左右间距) |
| Border 样式 | `ClipboardCard`（见第2节） |
| 双击 | 触发粘贴（复制到剪贴板 + 模拟Ctrl+V） |

### Row 0 — 顶部行：类型图标 + 操作按钮

**左侧（Column 0）：类型图标 + Pin角标**

| 元素 | 属性 | 值 |
|------|------|------|
| 类型图标 | Text | `{Binding TypeIcon}` |
| 类型图标 | FontSize | `18` |
| 类型图标 | Opacity | `0.9` |
| Pin角标（圆点） | Width×Height | `8×8` |
| Pin角标 | CornerRadius | `4` |
| Pin角标 | Background | `PinnedBrush` (#E8A838) |
| Pin角标 | Margin（左间距） | `7,0,0,0` |
| Pin角标 | 可见性 | `IsPinned` 为 true 时显示 |

**右侧（Column 1）：操作按钮区（悬停显示）**

| 属性 | 值 |
|------|------|
| 默认 Opacity | `0`（隐藏） |
| 悬停 Opacity | `1`（显示） |
| 淡入淡出时长 | `0.15秒` |
| 按钮1 | `📌` 置顶 (FontSize=15, Margin右=4) |
| 按钮2 | `🗑` 删除 (FontSize=15) |

> **注意**: 分组功能已从卡片按钮移至右键菜单。

### 类型图标对照

| 类型 | 图标 | 枚举值 |
|------|------|--------|
| 纯文本 | `📝` | Text (0) |
| 图片 | `🖼` | Image (1) |
| 文件 | `📁` | Files (2) |
| 富文本 | `📄` | Rtf (3) |
| HTML | `🌐` | Html (4) |

### Row 1 — 内容预览区（DataTrigger 自动切换）

**显示逻辑**：`Image` 属性不为 null → 显示图片；为 null → 显示文本

#### 文本预览（`PreviewTextBox`）

| 属性 | 值 |
|------|------|
| 可见性 | `Image == null` 时 Visible，否则 Collapsed |
| Text | `{Binding PreviewText}` |
| FontSize | `14` |
| Foreground | `#F0F1F5` |
| TextTrimming | `CharacterEllipsis` |
| TextWrapping | `Wrap` |
| MaxHeight | `130` |
| LineHeight | `20` |

#### 图片预览（`PreviewImageBox`）

| 属性 | 值 |
|------|------|
| 可见性 | `Image != null` 时 Visible，否则 Collapsed |
| Source | `{Binding Image}` |
| MaxHeight | `220` |
| Stretch | `Uniform` |
| HorizontalAlignment | `Center` |

> **图片来源**：①直接复制的图片（Clipboard Image）②复制的图片文件自动加载缩略图（200px解码宽度）

### Row 2 — 底部行：时间戳 + 标签

| 元素 | 属性 | 值 |
|------|------|------|
| 时间文字 | Text | `{Binding TimeText}`（智能格式：今天/昨天/MM-dd/yyyy-MM-dd + HH:mm） |
| 时间文字 | FontSize | `11` |
| 时间文字 | Foreground | `#8B8D98` |
| 标签背景 | CornerRadius | `4` |
| 标签背景 | Padding | `7,2` |
| 标签背景 | Background | `AccentBrush` (#5B8DEF) |
| 标签背景 | Opacity | `0.9` |
| 标签背景 | Margin（左间距） | `7,0,0,0` |
| 标签背景 | 可见性 | Tag 非空时显示（`TagToVisibilityConverter`） |
| 标签文字 | Text | `{Binding Tag}` |
| 标签文字 | FontSize | `10` |
| 标签文字 | FontWeight | `Medium` |
| 标签文字 | Foreground | `White` |

---

## 7. 右键上下文菜单

**触发方式**: 在任意剪贴板卡片上**鼠标右键点击**

### 菜单结构

```
┌──────────────────────┐
│ 📌 置顶              │  ← 切换置顶状态
├──────────────────────┤  (Separator)
│ 🏷 分组           ▸  │  ← 鼠标悬停展开子菜单
│  ┌──────────────────┐│
│  │ ✕ 取消分组       ││  ← 仅当前项已有分组时显示
│  │ ──────────────── ││  (Separator)
│  │ ✓ 2              ││  ← 当前所在分组（加粗+勾选）
│  │   3              ││  ← 其他已创建的分组
│  │ ──────────────── ││  (Separator)
│  │ ＋ 新建分组…     ││  ← 弹出输入对话框（蓝色文字）
│  └──────────────────┘│
├──────────────────────┤  (Separator)
│ 🗑 删除              │  ← 删除该项
└──────────────────────┘
```

### 菜单项详情

| 菜单项 | 触发行为 | 条件显示 |
|--------|----------|----------|
| 📌 置顶 | 切换 `IsPinned` 状态 | 始终显示 |
| 🏷 分组 ▸ | 展开子菜单（鼠标悬停） | 始终显示 |
| ─ ✕ 取消分组 | 将该项 Tag 设为 null | 仅当前项 `Tag` 非空时显示 |
| ─ ✓ {分组名} | 将该项加入该分组（当前分组加粗+✓） | 列出所有已创建分组 |
| ─ {分组名} | 将该项加入该分组 | 列出所有已创建分组 |
| ─ ＋ 新建分组… | 弹出 `TagInputDialog` 输入新分组名 | 始终显示（子菜单末尾） |
| 🗑 删除 | 从历史中删除该项 | 始终显示 |

### 子菜单动态构建逻辑

1. 右键菜单 `Opened` 事件触发
2. 从 `PopupWindow.DataContext` (PopupViewModel) 获取 `Tags` 列表
3. 清空 `TagMenuItem.Items`，按顺序填充：
   - [条件] ✕ 取消分组 → Separator
   - [循环] 已有分组列表（当前分组加 ✓ 前缀 + 加粗）
   - Separator
   - ＋ 新建分组…（蓝色强调色文字）

### 分组管理交互

| 操作 | 触发位置 | 行为 |
|------|----------|------|
| 新建分组 | 右键菜单 → 分组 → ＋ 新建分组… | 弹出 `TagInputDialog` 输入框 |
| 设置分组 | 右键菜单 → 分组 → 分组名 | 立即设置，卡片底部标签更新 |
| 取消分组 | 右键菜单 → 分组 → ✕ 取消分组 | 立即清除 |
| 筛选分组 | 顶部标签栏点击分组名 | 切换显示该分组的项目 |
| 删除分组 | 顶部标签栏点击分组名旁的 ✕ | 删除该分组（不影响已标记的项） |

---

## 8. 交互动效与行为

### 全局热键

| 热键 | 行为 |
|------|------|
| `Ctrl+Shift+V` | 切换面板显示/隐藏 |

### 面板交互

| 操作 | 行为 |
|------|------|
| 面板外点击（Deactivate） | 自动隐藏面板 |
| `Esc` 键 | 隐藏面板 |
| 鼠标滚轮（在卡片区域） | 横向滚动卡片列表 |
| 鼠标滚轮（在标签栏） | 横向滚动标签栏 |

### 卡片交互

| 操作 | 行为 |
|------|------|
| 单击卡片 | 复制内容到剪贴板（不关闭面板） |
| 双击卡片 | 复制 + 立即关闭面板 + 模拟 Ctrl+V 粘贴 |
| 悬停卡片 | 卡片背景变亮 + 边框变亮 + 显示操作按钮（📌🗑） |
| 右键卡片 | 弹出上下文菜单 |
| 点击 📌 | 切换置顶状态（置顶项排最前） |
| 点击 🗑 | 删除该项 |

### 粘贴流程优化

1. 用户双击 → 先隐藏面板（用户感知 0ms）
2. 使用 Win32 API (`FastClipboard`) 直接写入剪贴板（~30ms）
3. 模拟 `Ctrl+V` 按键粘贴到当前焦点窗口
4. 全流程 ~50ms，无卡顿

### 数据持久化

| 数据 | 存储位置 | 格式 |
|------|----------|------|
| 剪贴板历史 | `%LocalAppData%\ClipVault\history.json` | JSON |
| 图片文件 | `%LocalAppData%\ClipVault\images\{guid}.png` | PNG |
| 最大容量 | 500 条 | FIFO 淘汰（置顶项不受限） |
| 自动清理 | 启动时 + 每小时 | 删除过期项 + 孤立图片 |

---

## 修改指引

你可以直接修改上方任意表格中的值，例如：

```
| CardBackgroundColor | `#1A1A2E` ← 修改这里 |
```

也可以描述你想要的风格变化，例如：
- "卡片圆角改成 16px"
- "搜索框宽度改为 200px"
- "去掉标签栏的删除 ✕ 按钮"
- "右键菜单增加复制选项"

AI 会根据你的修改来更新所有对应的 XAML 文件。
