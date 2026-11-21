namespace MyDebugTools.Samples

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open MyDebugTools.Contracts

type Workbench() =
    interface IDesktopPlugin with
        member _.Activate registry =
            let options = [| "请求 A"; "请求 B" |]
            let mutable selected = options[0]
            registry.MenuPoint {
                PageId = "sample.workbench"; PointId = "selection"; Version = 1
                Selection = fun () -> selected
            }
            registry.Page {
                Id = "sample.workbench"
                Title = "工作台"
                Create = fun () ->
                    StackPanel.create [
                        StackPanel.spacing 10.
                        StackPanel.children [
                            TextBlock.create [ TextBlock.text "工作台"; TextBlock.fontSize 24. ]
                            ComboBox.create [
                                ComboBox.dataItems options
                                ComboBox.selectedIndex 0
                                ComboBox.onSelectedIndexChanged (fun index -> if index >= 0 then selected <- options[index])
                            ]
                            TextBlock.create [ TextBlock.text "草稿" ]
                            TextBox.create [ TextBox.name "Draft"; TextBox.placeHolderText "输入待处理的内容" ]
                        ]
                    ] |> VirtualDom.create
            }
