module ThirdParty.PrivateText

let edition () =
#if EDITION_B
    "B"
#else
    "A"
#endif

let version () =
#if REVISION_TWO
    "v2"
#else
    "v1"
#endif
