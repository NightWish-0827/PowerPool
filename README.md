# PowerPool – High-Performance Unity Object Pooling SDK

![](https://img.shields.io/badge/unity-2021.3%2B-black)
![](https://img.shields.io/badge/license-MIT-blue)

> **PowerPool** is a high-performance object pooling framework for Unity designed for
> **zero-allocation runtime**,  
> **O(1) return complexity**, and **memory-safe lifecycle control**.

By combining **stack-allocated builder patterns**, **version-based safety validation**,
and **cache-friendly data structures**, PowerPool enables extremely efficient
runtime object spawning without the traditional GC or lookup overhead found
in typical pooling implementations.

Unlike conventional pooling systems that simply enable/disable objects,
PowerPool provides a **fully engineered lifecycle architecture** focused on
**performance**, **memory safety**, and **developer usability**.

<img width="797" height="90" alt="image" src="https://github.com/user-attachments/assets/67a707ab-46cb-4bb6-bc4a-39dfe6650b54" />  

`https://github.com/NightWish-0827/PowerPool.git?path=/com.nightwishlab.powerpool`  
UPM Add package from git URL

---

# Table of Contents

* [Core Features](#core-features)

* [API Reference & Usage](#api-reference--usage)

* [Lifecycle & Internal Architecture](#lifecycle--internal-architecture)

* [Telemetry & Runtime Statistics](#telemetry--runtime-statistics)

* [Editor Supports](#editor-supports)

* [Editor Case](#editor-case)
---

# Core Features

### Zero Allocation Fluent Builder

PowerPool introduces a **stack-allocated builder pattern** using `ref struct`.

This allows developers to configure spawn parameters such as **position**,
**rotation**, **parent transform**, and **initialization callbacks** using a
clean fluent API without generating **any heap allocations**.

---

### GC-Free Runtime Operation

Object **Rent** and **Return** operations are completely **allocation-free**.

Returned handles are implemented using a lightweight
`readonly struct PoolHandle<T>` to avoid unnecessary memory allocations.

This ensures that object pooling operations never trigger **garbage collection spikes**.

---

### O(1) Return Complexity

Traditional pooling systems often require a **Dictionary lookup** to determine
which pool an object belongs to during return operations.

PowerPool eliminates this overhead.

Each `PoolHandle<T>` internally stores a direct reference to its
**source pool instance**, allowing objects to return **immediately in constant time O(1)**.

No hash lookups.
No type checks.
No runtime pool discovery.

---

### Anti-Zombie Object Protection

One of the most common and dangerous bugs in pooling systems is the
**zombie reference problem** — when code continues to access an object
that has already been returned to the pool.

PowerPool prevents this entirely using a **generation-based validation system**.

Each pooled object is tracked by a **PowerPoolTracker** containing a
monotonically increasing **Version number**.

When a handle is issued:

* The current version is copied into the handle
* On return, the tracker version increments

If an outdated or already returned handle attempts access:

* Version mismatch is detected
* The operation is blocked safely
* Optional warning or exception policies can be triggered

---

### Cache-Friendly Parallel Array Architecture

PowerPoolCore uses a **parallel array layout** internally.

```
_items[]      → pooled components
_trackers[]   → lifecycle trackers
```

Both arrays share identical indices, enabling extremely fast lookup and
cache-friendly iteration during runtime operations.

This **data-oriented structure** significantly improves performance compared
to traditional object-oriented pool implementations.

---

### Zero Runtime GetComponent Overhead

Components implementing the `IPoolCleanable` interface are cached once during
object creation.

```
GetComponentsInChildren<IPoolCleanable>(true)
```

This occurs only during **prewarm / instantiate time**.

When an object returns to the pool:

```
OnReturnToPool()
```

is called directly from the cached array without any runtime component search.

---

### Independent Prefab Pool Isolation

PowerPool distinguishes pools not only by component type `<T>` but also by
**Prefab InstanceID**.

```
prefab.gameObject.GetInstanceID()
```

This guarantees that different prefabs sharing the same component type
are always managed in **separate independent pools**.

Example:

```
SlimePrefab  → Pool A
OrcPrefab    → Pool B
```

Even if both use the same `Monster` script.

---

### Ghost Object Tracking

If a pooled object is destroyed externally via `Destroy()` or during
scene transitions, PowerPoolTracker automatically detects the event.

```
OnUnexpectedDestroyed
```

This helps developers identify lifecycle inconsistencies that could
otherwise lead to memory leaks or corrupted pool states.

---

# API Reference & Usage

PowerPool’s API is designed around **three core concepts**:

* **Fluent Spawn Builder**
* **Handle-based lifecycle management**
* **Safe and deterministic object return**

Unlike traditional pooling APIs that expose multiple static functions,
PowerPool provides a **structured spawning pipeline** that keeps object creation
**expressive, safe, and allocation-free**.

---

## Fluent Spawn Builder

Objects are spawned through a **fluent builder pipeline**.

The builder is implemented as a **`ref struct`**, which guarantees:

* **Stack-only allocation**
* **Zero GC pressure**
* **Immediate execution**

Because `ref struct` values cannot be stored or captured, the API naturally
encourages **correct one-shot usage patterns**.

```csharp
PoolHandle<MyComponent> handle =
    PowerPool.Spawn(myPrefab)
        .At(position, rotation)
        .Under(parentTransform)
        .Rent();
```

This design ensures that **no intermediate heap allocations** are created while
still providing a highly readable configuration flow.

---

## Spawn Configuration Pipeline

The builder allows flexible configuration before the final `Rent()` call.

### Transform Placement

```csharp
var handle = PowerPool.Spawn(prefab)
    .At(spawnPosition, spawnRotation)
    .Under(parentTransform)
    .Rent();
```

Supported transform controls include:

* spawn position
* spawn rotation
* optional parent transform

---

### Initialization Callbacks

Additional runtime state can be injected during spawn using
`With()` or `WithState()`.

```csharp
var handle = PowerPool.Spawn(prefab)
    .At(pos, rot)
    .WithState(
        state: new SpawnState(color, impulse),
        onInitialize: static (item, s) =>
        {
            var st = (SpawnState)s;
            item.Init(st.Color, st.Impulse);
        })
    .Rent();
```

This pattern allows developers to **inject contextual data into pooled objects**
without requiring extra component lookups or global state.

Because the callback can be declared `static`, **closure allocations are avoided**.

---

## Handle-Based Lifecycle

Instead of returning raw objects, PowerPool returns a **`PoolHandle<T>`**.

```csharp
PoolHandle<MyComponent> handle = PowerPool.Spawn(prefab).Rent();
```

The handle contains:

* a reference to the **source pool**
* the object's **tracker**
* the **issued version**

This allows the system to perform **safe lifecycle validation** and detect
incorrect usage such as:

* double returns
* zombie references
* stale handles

---

## Returning Objects

Objects can be returned in multiple ways depending on the desired lifecycle.

### Explicit Return

```csharp
PowerPool.Return(ref handle);
```

The `ref` return pattern ensures that the handle becomes **invalid immediately**
after returning, preventing accidental reuse.

---

### Instance Method Return

```csharp
handle.Return();
```

This is the simplest and most common pattern when the handle is stored locally.

---

### Scoped Lifetime (using)

Handles implement `IDisposable`, allowing automatic scope-based returns.

```csharp
using (var fx = PowerPool.Spawn(effectPrefab).Rent())
{
    // temporary effect
}
```

When the scope ends, the object is **automatically returned to the pool**.

---

## Resetting Objects with IPoolCleanable

Objects can define their own reset logic by implementing `IPoolCleanable`.

```csharp
public class MyEntity : MonoBehaviour, IPoolCleanable
{
    public void OnReturnToPool()
    {
        // Reset state
        // Clear buffs
        // Restore HP
    }
}
```

PowerPool calls `OnReturnToPool()` automatically during the return process.

To avoid runtime overhead, the required components are **cached once during
object creation**, eliminating the need for repeated `GetComponent` calls.

---

## Prewarming and Pool Registration

Pools can be created lazily during the first spawn, but it is often preferable
to **prewarm them during initialization**.

```csharp
PowerPool.Register(prefab, initialCapacity, poolRoot);
```

Optional parameters allow developers to configure:

* initial capacity
* pool root transform
* maximum pool size
* overflow policy

Example:

```csharp
PowerPool.Register(
    prefab,
    initialCapacity: 32,
    poolRoot: poolTransform,
    maxPoolSize: 256,
    overflowPolicy: PowerPoolOverflowPolicy.Expand);
```

Prewarming prevents runtime spikes caused by object instantiation.

---

## Runtime Statistics

PowerPool exposes detailed runtime metrics via `PowerPoolStats`.

```csharp
if (PowerPool.TryGetStats(prefab, out var stats))
{
    Debug.Log(
        $"Created={stats.TotalCreated}, " +
        $"InPool={stats.InPool}, " +
        $"Active={stats.ActiveEstimated}");
}
```

Statistics include:

* total objects created
* active objects
* pooled objects
* rent / return call counts
* pool capacity
* expansion events
* overflow policy configuration

This allows developers to monitor **runtime pool behavior** and tune memory usage.

---

## Asynchronous / Delayed Return Patterns

For delayed return scenarios such as **coroutines, timers, or async logic**,
handles can be safely stored in lightweight wrapper objects.

```csharp
var handle = PowerPool.Spawn(prefab).Rent();

StartCoroutine(ReturnLater(handle, 2f));
```

Inside the coroutine:

```csharp
PowerPool.Return(ref handle);
```

Because the handle internally tracks **version and ownership**, PowerPool
remains protected against stale or duplicated return operations.

---

### In Practice

PowerPool is intentionally designed to be **flexible rather than restrictive**.

You can use it for:

* manual lifecycle control
* temporary scoped objects
* coroutine-based effects
* high-frequency projectile spawning
* large-scale entity systems

The fluent builder and handle system allow developers to balance
**performance**, **safety**, and **API readability** without sacrificing
runtime efficiency.

---

# Lifecycle & Internal Architecture

PowerPool manages object lifecycles using a structured runtime pipeline.

```
Pool Initialization
-------------------
PowerPool.Spawn(prefab)
→ Locate or create pool instance
→ Lazy initialization if necessary

Prewarm Phase
-------------
EnsureCapacity / PrewarmAdditional
→ Allocate pool memory ahead of time
→ Prevent runtime allocation spikes

Rent Phase
----------
PoolBuilder<T>.Rent()
→ Retrieve inactive object
→ Assign tracker version
→ Activate object

Return Phase
------------
PoolHandle.Return()
→ Version validation
→ Call IPoolCleanable reset hooks
→ Disable and return to pool

Overflow Handling
-----------------
When pool reaches MaxPoolSize:

Expand
→ dynamically allocate additional objects

DestroyOnReturn
→ destroy objects when returned if capacity exceeded
```

---

# Telemetry & Runtime Statistics

PowerPool exposes detailed runtime telemetry through
the `PowerPoolStats` structure.

Developers can monitor:

* total objects created
* currently pooled objects
* active objects in scene
* unexpected destruction events
* runtime pool expansion

This allows teams to **analyze memory usage and pooling behavior in real time**.

---

# Editor Supports

> Provide Tracker Window at Editor level It allows you to track the states of objects.

<img width="447" height="276" alt="image" src="https://github.com/user-attachments/assets/579b2a3c-4eba-4977-8eb7-5bcac7c38541" />  

<img width="750" height="397" alt="image" src="https://github.com/user-attachments/assets/27999d2b-dd44-4c21-bc89-5e2b43fc4df6" />

---

# Editor Case 

> When the **Editor** tests uses **PlayMode**, the following errors or warnings may occur.  

`[Powerful] An object that failed to pass through the pool was forcibly destroyed. Be careful of memory leakage/reference leakage.`  
: **LogWarning**

`[Powerful] Handle is invalid or has already been returned. (Tracer not found (default handle or external manipulation)`  
: **LogError**

This is the noise from the Stop call on the **Editor**, **so it is safe to ignore**.

However, if this occurs in a Dev Build or non-forced shutdown situation, the Pool Manager or object return cycle you designed is not complete.

---

### In a word...

PowerPool is not just another object pool.

It is a **fully engineered runtime system** designed to make
Unity object pooling **safe**, **deterministic**, and **extremely fast**.

By combining **zero-allocation patterns**, **generation-based safety**,
and **cache-optimized data structures**, PowerPool delivers a pooling
architecture capable of supporting even the most demanding runtime workloads.

**High performance pooling should be invisible.**

PowerPool makes that possible.
