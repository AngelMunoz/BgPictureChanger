// Learn more about F# at http://fsharp.org

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open CliWrap


[<EntryPoint>]
let main argv =
    let refresh =
        argv
        |> Array.tryFind (fun str -> str.Contains("--refresh="))
        |> Option.map (fun refresh -> refresh.Substring("--refresh=".Length))
        |> Option.orElse
            (Environment.GetEnvironmentVariable("BgPictureChanger_REFRESH_RATE")
             |> Option.ofObj)
        |> Option.orElse (Some "1h")
        |> Option.map (fun str ->
            let str = if str.Length <= 1 then "1h" else str

            match str.[str.Length - 1] with
            | 's' ->
                match Decimal.TryParse(str.Substring(0, str.Length - 2) |> string) with
                | true, value ->
                    TimeSpan
                        .FromSeconds(value |> float)
                        .TotalMilliseconds
                | false, _ -> TimeSpan.FromSeconds(30.).TotalMilliseconds
            | 'm' ->
                match Decimal.TryParse(str.Substring(0, str.Length - 2) |> string) with
                | true, value ->
                    TimeSpan
                        .FromMinutes(value |> float)
                        .TotalMilliseconds
                | false, _ -> TimeSpan.FromMinutes(30.).TotalMilliseconds
            | 'h' ->
                match Decimal.TryParse(str.Substring(0, str.Length - 2) |> string) with
                | true, value ->
                    TimeSpan
                        .FromHours(value |> float)
                        .TotalMilliseconds
                | false, _ -> TimeSpan.FromHours(1.).TotalMilliseconds
            | 'd' ->
                match Decimal.TryParse(str.Substring(0, str.Length - 2) |> string) with
                | true, value ->
                    TimeSpan
                        .FromDays(value |> float)
                        .TotalMilliseconds
                | false, _ -> TimeSpan.FromDays(1.).TotalMilliseconds
            | 'w' ->
                match Decimal.TryParse(str.Substring(0, str.Length - 2) |> string) with
                | true, value ->
                    TimeSpan
                        .FromDays((value |> float) * 7.)
                        .TotalMilliseconds
                | false, _ -> TimeSpan.FromDays(7.).TotalMilliseconds
            | _ -> TimeSpan.FromHours(1.).TotalMilliseconds)
        |> Option.get

    let picturesDir =
        argv
        |> Array.tryFind (fun str -> str.Contains("--pictures="))
        |> Option.map (fun pathlike -> pathlike.Substring("--pictures=".Length))
        |> Option.orElse
            (Environment.GetEnvironmentVariable("BgPictureChanger_Pictures_PATH")
             |> Option.ofObj)
        |> Option.orElse
            (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
             |> Option.ofObj)
        |> Option.orElse
            (Environment.GetEnvironmentVariable("HOME")
             |> Option.ofObj
             |> Option.map (fun home -> sprintf "%s/Pictures/" home))
        |> Option.map (fun pathlike -> Path.GetFullPath(pathlike))
        |> Option.get

    use writer =
        let path =
            argv
            |> Array.tryFind (fun str -> str.Contains("--logpath="))
            |> Option.map (fun pathlike -> pathlike.Substring("--logpath=".Length))
            |> Option.orElse
                (Environment.GetEnvironmentVariable("HOME")
                 |> Option.ofObj
                 |> Option.map (fun home -> sprintf "%s/.config/.changebg.log" home))
            |> Option.map (fun pathlike -> Path.GetFullPath(pathlike))
            |> Option.get

        try
            new StreamWriter(File.Open(path, FileMode.Append))
        with :? System.IO.DirectoryNotFoundException -> File.CreateText(path)

    writer.AutoFlush <- true
    Console.SetOut(writer)
    Console.SetError(writer)
    printfn "Starting Program [%s - %s]" (DateTime.Now.ToLongDateString()) (DateTime.Now.ToLongTimeString())


    let getPath (file: FileInfo) = sprintf "file://%s" file.FullName

    let getRandomIndex (fileCount: int) =
        RandomNumberGenerator.GetInt32(fileCount)



    async {
        do! Async.SwitchToThreadPool()

        try
            let randomPicture () =
                let dir () =
                    try
                        let dir =
                            DirectoryInfo(Path.GetFullPath(picturesDir))

                        dir.EnumerateDirectories() |> ignore
                        dir
                    with :? System.IO.DirectoryNotFoundException as ex ->
                        eprintfn "\n%O\nDirectory Not Found, Falling back to Home Pictures..." ex

                        let picturesDir =
                            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                            |> Option.ofObj
                            |> Option.map (fun pathlike -> Path.GetFullPath(pathlike))
                            |> Option.get

                        DirectoryInfo(Path.GetFullPath(picturesDir))

                let pictures = dir().EnumerateFiles()

                if pictures |> Seq.length <= 0 then
                    None
                else
                    let index = getRandomIndex (pictures |> Seq.length)
                    Some(getPath (pictures |> Seq.item index))

            while true do
                printfn "Running in process: [%i]" (Process.GetCurrentProcess().Id)

                match randomPicture () with
                | Some path ->
                    let args =
                        let str = sprintf """set org.gnome.desktop.background picture-uri "%s" """ path
                        str.TrimEnd()

                    printfn "Executing [/usr/bin/gsettings %s]" args

                    let! result =
                        Cli
                            .Wrap("/usr/bin/gsettings")
                            .WithArguments(args)
                            .ExecuteAsync()
                            .Task
                        |> Async.AwaitTask

                    printfn "Changed Picture At: %s\n" (DateTime.Now.ToLongTimeString())
                    if result.ExitCode <> 0 then
                        eprintfn "Couldn't change the Picture, will retry later..."
                | None -> eprintfn "No Pictures were found, will check again later"

                do! Async.Sleep(refresh |> int)
        with ex ->
            eprintfn "Unhandled error\n\n%O" ex
            writer.Flush()
            writer.Dispose()

        return 0
    }
    |> Async.RunSynchronously
