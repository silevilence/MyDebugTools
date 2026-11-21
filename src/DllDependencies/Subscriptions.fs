module internal MyDebugTools.DllDependencies.Subscriptions

open System

// Windows UI Automation can retain peers for controls already removed from
// the visual tree. Disconnect their callbacks when a result or tab retires.
type Group() =
    let subscriptions = ResizeArray<IDisposable>()
    member _.Add(subscription) = subscriptions.Add subscription
    member _.Clear() =
        for subscription in subscriptions do subscription.Dispose()
        subscriptions.Clear()
