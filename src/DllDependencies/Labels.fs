module internal MyDebugTools.DllDependencies.Labels

open MyDebugTools.DllDebugger

let moduleLabel (m: DependencyModule) = $"{m.Path} · {m.Architecture}"

let edgeLabel (edge: DependencyEdge) =
    let kind = if edge.Delayed then "延迟导入" else "普通导入"
    let relation =
        match edge.Reference with
        | DependencyReference.Shared -> " · 共享引用"
        | DependencyReference.Cycle -> " · 循环引用"
        | _ -> ""
    let destination = if isNull edge.Target then edge.Problem else moduleLabel edge.Target
    let contract =
        if not edge.IsApiSet then ""
        elif isNull edge.ApiSetHost then " · API Set 合约"
        else $" · API Set 宿主 {edge.ApiSetHost}"
    $"{edge.Name} · {kind}{contract}{relation} · {destination}"
