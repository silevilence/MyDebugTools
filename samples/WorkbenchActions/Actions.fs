namespace MyDebugTools.Samples

open MyDebugTools.Contracts

type WorkbenchActions() =
    interface IDesktopPlugin with
        member _.Activate registry =
            registry.MenuAction {
                PageId = "sample.workbench"; PointId = "selection"
                MinVersion = 1; MaxVersion = 1; Title = "查看选中项"
                Invoke = fun selection -> $"当前选中：{selection}"
            }
