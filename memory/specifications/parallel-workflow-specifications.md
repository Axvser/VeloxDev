# 差分模式与跨会话锁

> 用户口述、agent 落笔。约束**代码在哪改**、**多个会话与子代理怎么互斥**。
> 触发：修改 `Src/`、`Src/Templates/`、`Examples/` 或 `SubRepos/` 下子仓库代码之前；创建差分副本、合并回主树、提交子仓库指针之前；派发子代理之前；运行 `dotnet new` 安装/卸载、NuGet 发布或其它机器级写操作之前。

---

## 一、子仓库（`SubRepos/`）

子仓库统一放在主仓根目录的 `SubRepos/<名>/`，以 submodule 形式登记在 `.gitmodules`；submodule 名、目录名与 `repo:<名>` 锁 scope 三者一致。子仓库清单见 `AGENTS.md` 的「子仓库与并行改动」表。本文件与 `AGENTS.md` 的要求对它们同样有效。

- 每个子仓库是独立 git 仓库；开发在 VeloxDev 体内统筹，主仓的规范（`AGENTS.md`、`memory/specifications/`、`skills/` 的密度要求、提交与版本规范）直接适用于 `SubRepos/<名>/` 下的改动，**不向子仓库复制这些规范**；子仓库只维护自己的 `memory/`（模块记忆在 `SubRepos/<名>/memory/modules/`）与 `skills/`（如有）。改动在哪个子仓库发生，就在那个子仓库提交，再更新父仓库指针。
- 新增子仓库：放到 `SubRepos/<名>/`，`.gitmodules` 的 `path` 写 `SubRepos/<名>`，在 `AGENTS.md` 索引表加一行，并在 §三 的 scope 表加 `repo:<名>`；`DevTools/verify_pushes.py` 从 `.gitmodules` 自动发现，无需登记。移动或改名后运行 `git submodule sync`。
- 新克隆后 `git submodule update --init --recursive`；Debug 构建走子仓库源码，Release 走 NuGet 包。
- 差分副本把子仓库展开成普通目录；合并时按 **父仓库 / 各子仓库** 拆补丁。父仓库补丁排除 `SubRepos/`；子仓库补丁在各自目录里用 `git apply -p3`（补丁路径以 `a/SubRepos/<名>/...` 开头）。
- 子仓库的提交信息同样用 `[类型][模块]` 格式；父仓库的 `memory/`、`skills/` 与子仓库自己的 `skills/` 都受记忆密度约束。

---

## 二、差分模式

**代码修改一律在主工作树之外完成。** 主工作树只用于读取基线、合并与提交。

### 1. 建差分副本

先拿 `main` 锁，复制当前工作树（含子仓库工作文件，不复制任何 `.git`）：

```powershell
python DevTools/repo_lock.py acquire --owner <session> --scope main --note "copy baseline"
robocopy . ..\VeloxDev-<slug> /E /XD .git bin obj .vs TestResults /XF .git *.user
python DevTools/repo_lock.py release --owner <session> --scope main
```

`robocopy` 的退出码 1–7 都是成功；`/XF .git` 同时排掉各子仓库根部的 `.git` 文件（那是指向父仓库 `.git/modules` 的指针，复制过去会坏）。

- 目标在仓库外（同级目录）；放在仓库内会被下一次复制带进去。
- 复制完成后在副本里 `git init`、`git add -A`、提交一个 `base:` 快照；把父仓库与各子仓库的基线 SHA 写进副本 `.git/veloxdev-base.json`。
- 副本里的子仓库是普通目录；不要在那里执行 `git submodule` 命令。

### 2. 改与验

- 所有子代理、构建、测试都在副本里进行。
- 一个会话一个 `VELOXDEV_SESSION_ID`；派发子代理时把它写进子代理的提示词与环境，子代理不另建 owner。
- 并行只发生在**不同模块的锁**之间；同一模块同一时间只允许一个会话。

### 3. 合并回主树

1. 生成补丁：父仓库（排除 `SubRepos/`）、每个子仓库各一份（保留前缀）。
2. 一次性申请需要的锁（脚本会排序；不要拿着锁再申请第二把）：

   ```powershell
   python DevTools/repo_lock.py acquire --owner <session> `
       --scope repo:Easel --scope module:Easel --scope main --wait 120
   ```

3. 按子仓库先、父仓库后的顺序应用并提交：
   - 在 `SubRepos/Easel/` 里 `git apply --check -p3 easel.patch`，通过后 `git apply -p3 easel.patch`，跑 Easel 测试，提交；
   - 更新父仓库的 `Easel` 指针；
   - 父仓库应用补丁，跑 Headless / 模板 harness / demo 构建，提交；
   - `git push` 仍按版本与记忆门禁检查；门禁脚本 `DevTools/verify_pushes.py` 同时扫描父仓库与 `SubRepos/` 下各子仓库。
4. 释放全部锁。

- 应用前先 `git apply --check`；失败就回副本修，不在主树手改。
- 主树构建会向 `Src/` 写生成文件；需要构建时在副本里构建。

---

## 三、跨会话锁

### 1. 租约模型

多个独立会话各自派子代理，争的是同一台机器上的主工作树、子仓库和全局工具。锁目录放在仓库外（默认 `%LOCALAPPDATA%\VeloxDev\locks`，用 `VELOXDEV_LOCK_ROOT` 覆盖），所有副本与工作树共享；不跨机器。

锁是**目录租约**：`mkdir` 原子成功即持有；默认 20 分钟，`renew` 续期；崩溃的会话租约到期后可被接管。

### 2. 作用域

| scope | 保护什么 | 什么时候拿 |
|---|---|---|
| `main` | 主工作树、父仓库提交/推送、版本号、子仓库指针、复制基线 | 合并、提交、复制主树时 |
| `repo:Easel` / `repo:Wiki` / `repo:AvalonMarkdown` | 各子仓库工作树的写入、提交、推送 | 合并该子仓库时 |
| `module:<名>` | 一个模块的编辑与合并；名字取 `Src/` 或 `memory/modules/` 的目录名（`Easel`、`WinForms`、`Core`、`Templates`、`Docs`…） | 动该模块前后 |
| `global` | `dotnet new` 安装/卸载、NuGet 发布/下架、其它机器级写操作 | 执行前 |

- 只锁实际要改的模块；跨模块任务一次申请整组。
- 申请顺序由脚本排序；**不要**先拿一把再在别处拿第二把。需要多把时一次 `acquire` 完，失败就全部回退后再等。
- 同一 owner 重复申请同一 scope 是重入；子代理可直接复用父会话的 owner。

### 3. CLI

```powershell
$env:VELOXDEV_SESSION_ID = "s-20261127-a"
python DevTools/repo_lock.py acquire --owner $env:VELOXDEV_SESSION_ID --scope module:WinForms --ttl 30
python DevTools/repo_lock.py renew  --owner $env:VELOXDEV_SESSION_ID --scope module:WinForms
python DevTools/repo_lock.py status
python DevTools/repo_lock.py release --owner $env:VELOXDEV_SESSION_ID --scope module:WinForms
```

```bash
export VELOXDEV_SESSION_ID=s-20261127-a
python DevTools/repo_lock.py acquire --scope module:WinForms --ttl 30
```

- `acquire --wait 120` 等别人释放；默认立即失败并打印持有者，适合改去另一个模块。
- `status --json` 给 agent 读，含 `remaining_seconds`。
- `release --force` 只在确认持有者已崩溃且用户同意时用；不要用它绕过正常等待。
- owner 唯一；父会话在派发子代理前设置 `VELOXDEV_SESSION_ID`，子代理继承。只有顶层会话 release，子代理不释放父会话的锁。
- 长任务在租约过半时 `renew`；会话结束（成功或失败）都 release。

---

## 四、自查

- [ ] 改动是否在差分副本里完成，主树只做了合并/提交？
- [ ] 动过的子仓库是否先在子仓库提交，再更新父指针？
- [ ] 申请的是最小 scope 集合，且一次申请、按脚本排序？
- [ ] 派发子代理前设置了 `VELOXDEV_SESSION_ID`，子代理没有另建 owner？
- [ ] 合并前 `git apply --check` 通过，合并后在副本里跑过对应测试？
- [ ] 会话结束释放了全部锁？
