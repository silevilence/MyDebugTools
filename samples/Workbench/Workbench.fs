namespace MyDebugTools.Samples

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open MyDebugTools.Contracts

type Workbench() =
    interface IDesktopPlugin with
        member _.Activate registry =
            registry.Page {
                Id = "sample.workbench"
                Title = "工作台"
                Create = fun () ->
                    StackPanel.create [
                        StackPanel.spacing 10.
                        StackPanel.children [
                            TextBlock.create [ TextBlock.text "工作台"; TextBlock.fontSize 24. ]
                            TextBlock.create [ TextBlock.text "草稿" ]
                            TextBox.create [ TextBox.name "Draft"; TextBox.placeHolderText "输入待处理的内容" ]
                        ]
                    ] |> VirtualDom.create
            }
