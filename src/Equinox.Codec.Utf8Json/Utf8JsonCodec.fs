namespace Equinox.Codec.Utf8Json

open Utf8Json
open Utf8Json.Resolvers
open Utf8Json.FSharp
open System.Runtime.InteropServices

/// Utf8Json implementation of TypeShape.UnionContractEncoder's IEncoder that encodes direct to a UTF-8 Buffer
[<RequireQualifiedAccess>]
type BytesEncoder(?resolvers: IJsonFormatterResolver []) =
    let resolvers = defaultArg resolvers [| FSharpResolver.Instance; StandardResolver.Default |]
    do CompositeResolver.RegisterAndSetAsDefault resolvers
    
    interface TypeShape.UnionContract.IEncoder<byte[]> with
        member __.Empty = Unchecked.defaultof<_>
        member __.Encode (value : 'T) =
            let bytes = JsonSerializer.Serialize value
            let obj = JsonSerializer.Deserialize<'T> bytes
            bytes
        member __.Decode(json : byte[]) =
            JsonSerializer.Deserialize json
            
/// Provides Codecs that render to a UTF-8 array suitable for storage in EventStore or CosmosDb based on explicit functions you supply using `Utf8Json` and 
/// TypeShape.UnionContract.UnionContractEncoder - if you need full control and/or have have your own codecs, see Equinox.Codec.JsonUtf8 instead
type Json =

    /// <summary>
    ///     Generate a codec suitable for use with <c>Equinox.EventStore</c> or <c>Equinox.Cosmos</c>,
    ///       using the supplied `Utf8Json` <c>resolvers</c>.
    ///     The Event Type Names are inferred based on either explicit `DataMember(Name=` Attributes,
    ///       or (if unspecified) the Discriminated Union Case Name
    ///     The Union must be tagged with `interface TypeShape.UnionContract.IUnionContract` to signify this scheme applies.
    ///     See https://github.com/eiriktsarpalis/TypeShape/blob/master/tests/TypeShape.Tests/UnionContractTests.fs for example usage.</summary>
    /// <param name="resolvers">Optional resolver array to be used by the underlying <c>Utf8Json</c> Serializer when encoding/decoding.
    /// Default are: FSharpResolver.Instance and StandardResolver.Default</param>
    /// <param name="allowNullaryCases">Fail encoder generation if union contains nullary cases. Defaults to <c>true</c>.</param>
    static member Create<'Union when 'Union :> TypeShape.UnionContract.IUnionContract>
        (   [<Optional;DefaultParameterValue(null)>]?resolvers,
            [<Optional;DefaultParameterValue(null)>]?allowNullaryCases)
        : Equinox.Codec.IUnionEncoder<'Union,byte[]> =
        let dataCodec =
            TypeShape.UnionContract.UnionContractEncoder.Create<'Union,byte[]>(
                new BytesEncoder(?resolvers = resolvers),
                requireRecordFields=true, // See JsonConverterTests - roundtripping correctly to UTF-8 with Json.net is painful so for now we lock up the dragons
                ?allowNullaryCases=allowNullaryCases)
        { new Equinox.Codec.IUnionEncoder<'Union,byte[]> with
            member __.Encode value =
                let enc = dataCodec.Encode value
                Equinox.Codec.Core.EventData.Create(enc.CaseName, enc.Payload) :> _
            member __.TryDecode encoded =
                dataCodec.TryDecode { CaseName = encoded.EventType; Payload = encoded.Data } }