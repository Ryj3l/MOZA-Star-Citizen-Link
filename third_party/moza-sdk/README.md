# MOZA SDK

Do not commit the full MOZA SDK to this repository.

For local development, install or extract the SDK outside the repo, or place it under:

```text
third_party/moza-sdk/sdk-local/
```

That folder is ignored by git.

The portable package script can copy the three runtime DLLs required by the dynamic C# provider:

- `MOZA_API_CSharp.dll`
- `MOZA_API_C.dll`
- `MOZA_SDK.dll`

By default it looks for them at:

```text
D:\MOZA_SDK\MOZA_SDK\SDK_CSharp\x64
```

Do not publish a public GitHub Release containing MOZA SDK DLLs until the SDK redistribution terms have been checked.
