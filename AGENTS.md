# AGENTS.md

本文件是仓库规范的**索引头**。

规范正文不在本文件 —— 在 [`memory/`](memory/)。本文件只回答三件事：**遇到什么情况读哪一份**、**模块记忆放在哪**、**新增规范时放在哪**。

---

## 两个目录的分工

| 目录 | 谁维护 | 是什么 |
|---|---|---|
| [`memory/specifications/`](memory/specifications/) | 用户口述，agent 落笔 | **规范手册**。约束 agent 怎么做事。效力来自用户认可，agent **不自行新增或改动** |
| [`memory/modules/`](memory/modules/) | **agent 自主维护** | **模块记忆**。记录代码是怎么回事、改它该走哪条路。见索引表第二行 |

---

## 索引

| 规范 | 手册 | 什么时候必须读 |
|---|---|---|
| 代码注释风格 | [memory/specifications/code-comment-specifications.md](memory/specifications/code-comment-specifications.md) | 新增、修改或评审 `Src/` 下任何 `.cs` 成员 / 函数体 / `.xaml` `.axaml` `.razor` 标记注释之前 |
| 提交信息 | [memory/specifications/commit-message-specifications.md](memory/specifications/commit-message-specifications.md) | **每次 `git commit` 之前** —— 首行是 `[类型][模块] 英文标题`，body 是 `[what]`/`[why]`/`[how]` 三段中文；不合规就是不合规，不要凭习惯写 |
| 模块记忆维护 | [memory/specifications/memory-maintenance-specifications.md](memory/specifications/memory-maintenance-specifications.md) | **每次任务收尾时** —— 把这一轮学到的东西落进对应模块；以及开始任务、需要先理解某个模块之前 |

---

## 模块记忆在哪

`memory/modules/<模块名>/`，每个模块至少一份 `architecture.md`（服务于快速理解代码）与一份 `extension.md`（服务于走对扩展路径）。

**找模块直接 `ls memory/modules/`** —— 模块名原样沿用 `Src/` 里的目录名或项目名，不需要索引。**不要新增模块索引文件**：那只会多一处会过期的地方。

---

## 怎么用

1. **先读手册，再动代码。** 上表命中时，读整份手册 —— 不要凭记忆套用别处的习惯。
2. **手册是唯一事实源。** 上表的一句话摘要与手册正文冲突时，以手册为准；摘要只用来决定要不要点进去。
3. **改动落在你碰过的行上。** 手册里若有「现状与规范不一致」的小节，它约束的是**你正在编辑的行**，不是整个文件，更不是整个仓库 —— 不要顺手做全仓回归。
4. **收尾时写记忆。** 任务做完，按 [模块记忆维护](memory/specifications/memory-maintenance-specifications.md) 把这一轮的知识落进 `memory/modules/` —— 落笔前先跑一次 `git log` 看这个模块最近怎么改的：提交信息里已经有 `[what]` / `[why]` / `[how]`，记忆要写的是它**装不下**的（当下的代码事实、扩展路径、半年后还成立的坑）。这是本职，不是额外工作。提交前用对应手册的自查清单过一遍。
5. **记忆要核，不要信。** 记忆为真，是**写它的时候**为真。按记忆里提到的文件名 / 方法名去核代码，核不到就说明它过期了。

---

## 怎么新增一份规范

规范由用户口述，agent 落笔 —— agent 不自行发明规范。

1. 在 `memory/specifications/` 下新建文件，文件名用小写连字符，以 `specifications` 结尾（如 `code-comment-specifications.md`）。
2. 在索引表里加一行：**规范名** + 相对链接 + **「什么时候必须读」**。
3. 触发时机必须写成 agent 自己能判断的条件（「动 `.cs` 成员之前」），而不是口号（「保持代码整洁」）—— 这一行要在**不打开手册**的前提下就能决定该不该打开手册。
