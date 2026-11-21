# 基于 F# 与 Avalonia.FuncUI 的动态热插拔扩展系统架构设计方案

> **已归档，禁止作为参考（2026-11-21）。** 本文仅保留作历史记录。后续需求讨论、架构设计、实现和评审均不得引用本文作为依据。宿主与插件的现行要求见[正式规格](../.scratch/0000-plugin-host/spec.md)。

## 一、 顶层架构概述（分层三态模型）

本系统旨在构建一个**在软件运行时无需重启即可对内置与外部第三方 .NET 程序集进行动态加载、深度介入、实时替换与完全卸载（Collectible Hot Unloading）**的插件化桌面应用体系。

系统自底向上划分为三层：

```
┌────────────────────────────────────────────────────────────────────────┐
│                   3. 响应式视图组合层 (FuncUI View Hierarchy)             │
│  • 槽位投影 (Slot Injection)   • 高阶装饰 (Decorators)  • 上下文协作视图 │
├────────────────────────────────────────────────────────────────────────┤
│             2. 逻辑微内核与扩展契约层 (Cordis 拓扑 + F# 契约)             │
│  • 级联作用域 (Scope/Fiber)    • 服务编排与事件总线    • 扩展点注册表    │
├────────────────────────────────────────────────────────────────────────┤
│             1. 物理隔离与动态装载层 (Collectible ALC Isolation)          │
│  • 独立程序集加载上下文 (ALC)  • 依赖隔离与私有解析    • 物理文件句柄释放 │
└────────────────────────────────────────────────────────────────────────┘
```

1. **物理隔离层（Assembly Isolation Layer）**：
   每个外部插件程序集独享一个可回收的上下文（Collectible `AssemblyLoadContext`），解决第三方依赖版本冲突，实现物理文件句柄的完全释放。
2. **逻辑微内核层（Microkernel & Contract Layer）**：
   基于 Cordis 核心理念（作用域树、服务拓扑、级联生命周期），定义强类型扩展契约，维护统一的扩展点注册表（Extension Registry）。
3. **响应式视图组合层（Composable UI Layer）**：
   依托 Avalonia.FuncUI 的函数式 DSL，将视图抽象为纯函数。通过“插槽组件（Slot Components）”与“高阶视图装饰器（View Decorators）”，实现插件对界面的无缝嵌入、上下文协作与包裹增强。

---

## 二、 关键技术栈与核心库选型

| 层次 | 选型 | 核心职责 |
| :--- | :--- | :--- |
| **物理装载** | `McMaster.NETCore.Plugins` | 封装 Collectible ALC，处理跨边界类型的安全共享与文件句柄彻底释放。 |
| **逻辑内核** | `CordiSharp` 或 原生 F# 简化微内核 | 负责插件上下文注入、级联生命周期追踪、事件分发与依赖反转。 |
| **UI 表现** | `Avalonia.FuncUI` / `FuncUI.Elmish` | 提供基于 F# 的跨平台声明式界面、响应式局部重绘与无状态组件管道。 |
| **契约共享** | `MyApp.PluginContract` (纯 F# 类库) | 独立于宿主与插件，仅包含接口、数据模型（Record/DU）与类型定义。 |

---

## 三、 架构设计目的与核心优势

### 1. 设计目的
* **真正的零重启热维护**：安装新功能、停用或覆盖升级已有插件 DLL 时，均在运行时即时生效，不锁死磁盘文件。
* **深度视图协作**：打破传统“插件只能返回独立面板”的局限，允许插件获取宿主局部上下文，甚至以中间件形式包装、拦截宿主已有视图。
* **内置与外部平权**：软件自身的预置功能与第三方开发者的外部扩展遵循完全一致的契约。

### 2. 核心优势
* **函数式 UI 天然切合“物理卸载”**：
  在经典 MVVM 中，双向绑定、静态样式树和复杂的模板引擎极易在底层造成难以排查的循环强引用；而在 FuncUI 中，**UI 本质是数据到视图的映射（纯函数计算）**。当插件卸载并通知 UI 刷新时，FuncUI 的差异比对引擎直接丢弃旧节点，切断所有跨程序集引用，GC 能够顺利回收整个 ALC。
* **高阶组合力**：
  在函数式体系下，视图增强退化为高阶函数组合（`IView -> IView`），避免了庞大的宿主控件重写与黑盒式 Hook。

---

## 四、 核心机制实现（如何达成设计要求）

### 1. 深度扩展的三种接入模式

插件向宿主申请接入时，不再局限于单一的静态面板输出，而是根据场景选择对应的能力通道：

```fsharp
namespace MyApp.PluginContract

open Avalonia.FuncUI.Types

// 模式 A：上下文协作视图（父视图把状态与操作句柄下发给插件）
type ContextualView<'TContext> = 'TContext -> IView

// 模式 B：高阶装饰器（插件拦截已有视图并予以增强/包裹）
type ViewDecorator = IView -> IView

// 模式 C：命令注入（将动作挂载到菜单、快捷键或工具栏）
type CommandContribution<'TState> = {
    Id: string
    Label: string
    CanExecute: 'TState -> bool
    Execute: 'TState -> unit
}
```

### 2. 双向通信与生命周期联动

```fsharp
type IPlugin =
    inherit System.IDisposable
    abstract member Id: string
    abstract member Name: string
    // 初始化时注入宿主作用域，插件在此注册能力、订阅总线
    abstract member Activate: ctx: IPluginContext -> unit

and IPluginContext =
    abstract member RegisterSlotView: slotId: string * viewFactory: (unit -> IView) -> unit
    abstract member RegisterContextView<'TCtx>: slotId: string * factory: ContextualView<'TCtx> -> unit
    abstract member RegisterDecorator: slotId: string * decorator: ViewDecorator -> unit
    abstract member SubscribeEvent<'T>: ( 'T -> unit ) -> System.IDisposable
```

### 3. 热卸载与回收闭环（Load & Unload Loop）

```
[用户触发卸载/热更新]
        │
        ▼
1. 触发插件 Dispose() ──────► 取消订阅宿主事件、终止后台 Task / 线程
        │
        ▼
2. 宿主清除注册项 ──────────► 从 Slot 映射表、装饰器管道中剔除该插件引用
        │
        ▼
3. 触发 FuncUI 重绘 ────────► Slot 重新计算，抛弃所有旧的 IView 引用
        │
        ▼
4. 释放 PluginLoader ───────► 调用 Loader.Dispose()，标记 ALC 为回收状态
        │
        ▼
5. 执行 GC 清理 ────────────► GC.Collect() 彻底释放 DLL 物理文件占用
```

---

## 五、 插件能力的边界界定（Scope of Capabilities & Constraints）

为保障宿主进程的安全、稳定性以及架构的纯粹性，必须清晰界定“哪些能力开放，哪些能力必须受限或禁止”：

### 1. 明确允许开放的能力（Allowed Capabilities）
* **声明式 UI 挂载**：在任意公开的 Slot 注入新界面、挂载菜单项、工具栏按钮及侧边栏。
* **数据驱动的局部上下文访问**：读取宿主显式传递进来的 `Context`（如当前激活文档的内容、选中的坐标、只读状态机）。
* **行为命令注入**：向宿主派发命令（通过回调或 Elmish 消息机制触发宿主状态更新）。
* **受控的事件订阅与发布**：基于微内核上下文总线，在插件自身生命周期内收发领域事件。
* **私有逻辑与三方库自裁决**：插件内部可随意引用自身的私有 NuGet 依赖（私有 JSON 库、网络库、算法库），不受宿主版本制约。

### 2. 严格受限的能力（Controlled Capabilities）
* **全局主应用状态的修改**：
  * **边界**：插件**不得**直接对宿主 Elmish 根 Model 进行变异（Mutation）。
  * **规范**：必须通过宿主下发的专属 Dispatcher 或上下文方法间接请求变更。
* **窗口级与顶级原生控件控制**：
  * **边界**：插件原则上只暴露组合视图 `IView`，不直接持有或修改宿主的 `Window`、`TopLevel` 原生实例。
  * **规范**：若需唤起弹窗、对话框，必须调用宿主提供的抽象对话框服务（`IDialogService`）。

### 3. 绝对禁止的行为（Forbidden Actions - 违者导致无法卸载或闪退）
* **禁止跨边界导出具体类型**：
  * 插件**严禁**将插件程序集内部定义的非基础类型暴露给宿主。宿主与插件交互必须 100% 走 `MyApp.PluginContract` 中声明的接口与类型。一旦宿主以强类型引用了插件的内部 Class，ALC 将永久无法卸载。
* **禁止无取消机制的后台常驻线程**：
  * 禁止启动脱离生命周期管理的 `Thread` 或未传递 `CancellationToken` 的无限循环 `Task`。
* **禁止向宿主静态（Static / Module Let）对象注册长期强引用**：
  * 任何跨模块订阅必须能随 `Dispose` 反注销，严禁在外部全局静态事件上留下事件处理闭包。
* **禁止破坏性 DOM 操作与全局样式重置**：
  * 禁止在视图内强制覆盖宿主全局 Application 级样式或主题基类，样式增强只能封装在局部视图或高阶装饰器内部。

---

## 六、 外部开发者接入与工程规范

为使第三方开发者顺利开发并保证主应用稳定性，需对外部插件工程制定统一的构建规范：

### 1. 项目配置规范（.fsproj）

第三方插件必须声明与宿主共享的基础库不打入私有输出目录：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework> <!-- 或与宿主严格对齐的版本 -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <!-- 基础契约库：仅编译期依赖，运行时由宿主提供 -->
    <ProjectReference Include="..\MyApp.PluginContract\MyApp.PluginContract.fsproj"
                      PrivateAssets="all"
                      ExcludeAssets="runtime" />

    <!-- FuncUI 框架：严禁在插件输出目录中包含独立副本 -->
    <PackageReference Include="Avalonia.FuncUI" Version="1.*"
                      PrivateAssets="all"
                      ExcludeAssets="runtime" />
  </ItemGroup>
</Project>
```

### 2. 插件实现模板范式

```fsharp
namespace SamplePlugin

open System
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open MyApp.PluginContract

type SampleExtensionPlugin() =
    let disposables = ResizeArray<IDisposable>()

    interface IPlugin with
        member _.Id = "sample.extension"
        member _.Name = "样例扩展插件"

        member _.Activate(ctx: IPluginContext) =
            // 1. 注册普通 Slot 视图
            ctx.RegisterSlotView("sidebar/bottom", fun () ->
                TextBlock.create [ TextBlock.text "来自扩展插件的底部组件" ] :> IView
            )

            // 2. 注册高阶装饰器（给工作区外层套一层边框）
            ctx.RegisterDecorator("workspace/wrapper", fun innerView ->
                Border.create [
                    Border.borderThickness 2.0
                    Border.borderBrush "#336699"
                    Border.child innerView
                ] :> IView
            )

        member _.Dispose() =
            // 严格清理所有申请的资源
            for d in disposables do d.Dispose()
            disposables.Clear()
```

---

## 七、 总结

本方案将 **.NET Core 现代化的物理隔离技术（Collectible ALC）** 与 **F# + Avalonia.FuncUI 的声明式组合哲学** 结合：
1. 底层解决了动态更新、不重启覆盖 DLL、依赖版本防冲突的工程难题；
2. 顶层打破了“仅独立视图”的限制，实现了“上下文感知挂载”与“高阶视图装饰”的深度协作；
3. 通过明确的系统边界与生命周期反注销纪律，杜绝了桌面端扩展系统最常见的内存泄漏与文件占用隐患。
