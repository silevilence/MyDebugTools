module MyDebugTools.WebApi.Tests.HistoryTests

open System
open System.IO
open MyDebugTools.WebApi
open Xunit

[<Fact>]
let ``independent snapshots survive reopen eviction and clearing``() =
    let dir = Directory.CreateTempSubdirectory()
    try
        let d = Library.save dir.FullName { Library.blank with Title = "订单"; Url = "http://localhost/items" }
        let record source value =
            let args = [|HttpTests.arg "查询" "id" value|]
            use req = Http.prepare "GET" d.Url args
            let entry = History.snapshot source req d.Body args
            History.start dir.FullName 3 entry
            History.finish dir.FullName entry.Id ("HTTP 200 · " + value)
            entry.Id
        let first = record d.Title "1001"
        let second = record d.Title "1002"
        let third = record "临时请求" "其他"
        let rows () = History.list dir.FullName
        Assert.Equal<string array>([|third; second; first|], rows() |> Array.map _.Id)
        let saved = rows()
        Assert.Contains("1001", saved[2].Detail)
        Assert.Contains("1002", saved[1].Result)
        let fourth = record d.Title "4"
        Assert.Equal<string array>([|fourth; third; second|], rows() |> Array.map _.Id)
        History.clear dir.FullName
        History.finish dir.FullName fourth "已取消"
        Assert.Empty(rows())
        Assert.Equal<Definition>(d, Assert.Single(Library.list dir.FullName))
        let afterClear = record d.Title "1001"
        Assert.Equal(afterClear, (Assert.Single(rows())).Id)
    finally dir.Delete(true)
