# Usage

```
Description:
  -= Run a specific method of a .NET Assembly =-

Usage:
  RunDotNetDll <assembly> [<method>] [options]

Arguments:
  <assembly>  The Assembly to run.
  <method>    The method to call. You can specify the metadata token too. eg: 
              Mynamespace.MyClass.EntryPoint or @0x06000001 []

Options:
  -p, --preload <preload>  An assembly to preload.
  --version                Show version information
  -?, -h, --help           Show help and usage information
```

# Credits
This Project is inspired by the original [RunDotNetDll](https://github.com/enkomio/RunDotNetDll).

