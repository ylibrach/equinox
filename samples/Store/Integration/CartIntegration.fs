﻿module Samples.Store.Integration.CartIntegration

open Equinox
open Equinox.Cosmos.Integration
open Equinox.EventStore
open Equinox.MemoryStore
open Swensen.Unquote

#nowarn "1182" // From hereon in, we may have some 'unused' privates (the tests)

let fold, initial = Domain.Cart.Folds.fold, Domain.Cart.Folds.initial
let snapshot = Domain.Cart.Folds.isOrigin, Domain.Cart.Folds.snapshot

let createMemoryStore () =
    // we want to validate that the JSON UTF8 is working happily
    new VolatileStore<byte[]>()
let createServiceMemory log store =
    Backend.Cart.Service(log, fun (id,opt) -> MemoryStore.Resolver(store, Domain.Cart.Events.codec, fold, initial).Resolve(id,?option=opt))

let codec = Domain.Cart.Events.codec

let resolveGesStreamWithRollingSnapshots gateway =
    fun (id,opt) -> EventStore.Resolver(gateway, codec, fold, initial, access = AccessStrategy.RollingSnapshots snapshot).Resolve(id,?option=opt)
let resolveGesStreamWithoutCustomAccessStrategy gateway =
    fun (id,opt) -> EventStore.Resolver(gateway, codec, fold, initial).Resolve(id,?option=opt)

let resolveCosmosStreamWithSnapshotStrategy gateway =
    fun (id,opt) -> Cosmos.Resolver(gateway, codec, fold, initial, Cosmos.CachingStrategy.NoCaching, Cosmos.AccessStrategy.Snapshot snapshot).Resolve(id,?option=opt)
let resolveCosmosStreamWithoutCustomAccessStrategy gateway =
    fun (id,opt) -> Cosmos.Resolver(gateway, codec, fold, initial, Cosmos.CachingStrategy.NoCaching, Cosmos.AccessStrategy.Unoptimized).Resolve(id,?option=opt)

let addAndThenRemoveItemsManyTimesExceptTheLastOne context cartId skuId (service: Backend.Cart.Service) count =
    service.FlowAsync(cartId, false, fun _ctx execute ->
        for i in 1..count do
            execute <| Domain.Cart.AddItem (context, skuId, i)
            if i <> count then
                execute <| Domain.Cart.RemoveItem (context, skuId))

type Tests(testOutputHelper) =
    let testOutput = TestOutputAdapter testOutputHelper
    let createLog () = createLogger testOutput

    let act service (context,cartId,skuId) = async {
        do! addAndThenRemoveItemsManyTimesExceptTheLastOne context cartId skuId service 5

        let! state = service.Read cartId
        test <@ 5 = match state with { items = [{ quantity = quantity }] } -> quantity | _ -> failwith "nope" @>
    }

    [<AutoData>]
    let ``Can roundtrip in Memory, correctly folding the events`` args = Async.RunSynchronously <| async {
        let log, store = createLog (), createMemoryStore ()
        let service = createServiceMemory log store
        do! act service args
    }

    let arrange connect choose resolveStream = async {
        let log = createLog ()
        let! conn = connect log
        let gateway = choose conn defaultBatchSize
        return Backend.Cart.Service(log, resolveStream gateway) }

    [<AutoData(SkipIfRequestedViaEnvironmentVariable="EQUINOX_INTEGRATION_SKIP_EVENTSTORE")>]
    let ``Can roundtrip against EventStore, correctly folding the events without compaction semantics`` args = Async.RunSynchronously <| async {
        let! service = arrange connectToLocalEventStoreNode createGesGateway resolveGesStreamWithoutCustomAccessStrategy
        do! act service args
    }

    [<AutoData(SkipIfRequestedViaEnvironmentVariable="EQUINOX_INTEGRATION_SKIP_EVENTSTORE")>]
    let ``Can roundtrip against EventStore, correctly folding the events with RollingSnapshots`` args = Async.RunSynchronously <| async {
        let! service = arrange connectToLocalEventStoreNode createGesGateway resolveGesStreamWithRollingSnapshots
        do! act service args
    }

    [<AutoData(SkipIfRequestedViaEnvironmentVariable="EQUINOX_INTEGRATION_SKIP_COSMOS")>]
    let ``Can roundtrip against Cosmos, correctly folding the events without custom access strategy`` args = Async.RunSynchronously <| async {
        let! service = arrange connectToSpecifiedCosmosOrSimulator createCosmosContext resolveCosmosStreamWithoutCustomAccessStrategy
        do! act service args
    }

    [<AutoData(SkipIfRequestedViaEnvironmentVariable="EQUINOX_INTEGRATION_SKIP_COSMOS")>]
    let ``Can roundtrip against Cosmos, correctly folding the events with With Snapshotting`` args = Async.RunSynchronously <| async {
        let! service = arrange connectToSpecifiedCosmosOrSimulator createCosmosContext resolveCosmosStreamWithSnapshotStrategy
        do! act service args
    }