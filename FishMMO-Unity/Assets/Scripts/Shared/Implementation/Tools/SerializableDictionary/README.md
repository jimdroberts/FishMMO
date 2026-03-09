# Serializable Dictionary

**Short description:** Provides generic, Unity-inspector-editable `SerializableDictionary<TKey, TValue>` and `SerializableHashSet<T>` collections that survive serialization, complete with a custom property drawer that detects duplicate and null key conflicts.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guides](#quick-start-guides)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

Unity cannot serialize standard `Dictionary<TKey, TValue>` or `HashSet<T>` types. They will not appear in the Inspector, will not be saved with the scene, and will not be instantiated at startup. The classic workaround — storing parallel key/value arrays and rebuilding on load — is error-prone and tedious.

This module solves the problem by providing:

| Class | Purpose |
|---|---|
| `SerializableDictionaryBase<TKey, TValue, TValueStorage>` | Abstract generic base that stores keys and values in parallel `SerializeField` arrays and reconstructs the internal `Dictionary` via `ISerializationCallbackReceiver`. |
| `SerializableDictionary<TKey, TValue>` | Concrete two-type-parameter dictionary for simple key/value pairs. |
| `SerializableDictionary<TKey, TValue, TValueStorage>` | Concrete three-type-parameter dictionary for dictionaries whose values are lists or arrays (requires a `Storage<T>` wrapper). |
| `SerializableHashSet<T>` | A serializable hash-set following the same pattern — stores values in a `SerializeField` array and reconstructs the `HashSet<T>` on deserialization. |
| `SerializableDictionaryPropertyDrawer` | A custom `PropertyDrawer` (Editor-only) that renders both dictionaries and hash-sets in the Inspector with add/remove buttons and conflict detection. |
| `SerializableDictionaryStoragePropertyDrawer` | A small companion drawer that unwraps `Storage<T>` fields so list/array values display cleanly. |

All types live in the `FishMMO.Shared` namespace and are used throughout FishMMO wherever inspector-editable associative containers are needed.

Originally based on the open-source project by Mathieu Le Ber (MIT License, 2017).

## Supported Platforms

| Platform | Supported |
|---|---|
| Windows | Yes |
| Linux | Yes |
| WebGL | Yes |

| Requirement | Version |
|---|---|
| Unity | 6.3 LTS |
| Scripting Backend | IL2CPP |

## Features

- **Inherits from `Dictionary<TKey, TValue>` / `HashSet<T>`** — instances can be used anywhere the standard collection interfaces are expected (`IDictionary<TKey, TValue>`, `ISet<T>`, `IDictionary`, `IEnumerable`, etc.).
- **Full `ISerializationCallbackReceiver` implementation** — `OnBeforeSerialize` flattens the collection to parallel arrays; `OnAfterDeserialize` rebuilds it, so Unity serialization round-trips correctly.
- **`ISerializable` support** — both collections implement `System.Runtime.Serialization.ISerializable` for .NET binary serialization (e.g., network transport, deep-clone).
- **`IDeserializationCallback` support** — `OnDeserialization` is forwarded to the inner collection.
- **`CopyFrom` helper** — `CopyFrom(IDictionary<TKey, TValue>)` and `CopyFrom(ISet<T>)` allow bulk-assigning values from regular collections.
- **Copy constructor** — both classes accept an existing collection in their constructor for one-step cloning.
- **Any serializable key/value type** — works with primitives, enums, `UnityEngine.Object` references, custom `[Serializable]` classes, structs, etc.
- **Inspector editing with no extra code** — the `[CustomPropertyDrawer]` is applied with `useForChildren = true`, so every subclass is drawn automatically.
- **Conflict detection** — the property drawer detects and warns about duplicate keys (warning icon + tooltip "Conflicting key, this entry will be lost") and null keys (warning icon + tooltip "Null key, this entry will be lost") directly in the Inspector to prevent silent data loss.
- **Add / Remove buttons** — toolbar-style plus and minus buttons for each entry.
- **Dictionary-of-lists / dictionary-of-arrays** — supported via a three-type-parameter variant and a `SerializableDictionary.Storage<T>` wrapper class that makes nested collections serializable.
- **Expandable complex values** — the drawer automatically switches between compact (key-value on one line) and expanded layouts depending on whether keys/values are complex (generic, Vector4, Quaternion).
- **Full `IDictionary` (non-generic) interface** — the base class also implements the non-generic `IDictionary` for interop with legacy APIs.

## Prerequisites

- **Unity 6.3 LTS** (or newer) with IL2CPP scripting backend.
- The module is part of the FishMMO shared codebase — no external package dependencies.

## Installation / Build

This is an integrated module within the FishMMO project. It is located at:

```
Assets/Scripts/Shared/Implementation/Tools/SerializableDictionary/
```

No separate installation steps are required. The scripts are compiled as part of the FishMMO shared assembly. The Editor drawer script is placed in the `Editor/` subfolder so it is automatically excluded from runtime builds.

## Quick Start Guides

### 1. Create a simple serializable dictionary

Define a non-generic subclass (required because Unity cannot serialize open generic types):

```csharp
using System;
using FishMMO.Shared;

[Serializable]
public class StringStringDictionary : SerializableDictionary<string, string> {}
```

Use it in a MonoBehaviour:

```csharp
using UnityEngine;

public class Example : MonoBehaviour
{
    public StringStringDictionary m_lookup;

    void Start()
    {
        if (m_lookup.TryGetValue("hello", out string value))
            Debug.Log(value);
    }
}
```

The dictionary will appear in the Inspector as a foldout with add/remove buttons.

### 2. Create a serializable hash-set

```csharp
using System;
using FishMMO.Shared;

[Serializable]
public class IntHashSet : SerializableHashSet<int> {}
```

### 3. Create a dictionary of lists

Because Unity cannot serialize arrays-of-arrays, create a `Storage` wrapper first:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

[Serializable]
public class ColorListStorage : SerializableDictionary.Storage<List<Color>> {}

[Serializable]
public class StringColorListDictionary : SerializableDictionary<string, List<Color>, ColorListStorage> {}
```

Access values directly without going through `.data`:

```csharp
public StringColorListDictionary m_colorStringListDict;

void Example()
{
    List<Color> colorList = m_colorStringListDict["myKey"];
}
```

### 4. Use CopyFrom or the copy constructor

```csharp
// CopyFrom — clears target and copies all entries
var source = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
mySerializableDict.CopyFrom(source);

// Copy constructor (must be re-declared in your subclass)
[Serializable]
public class StringColorDictionary : SerializableDictionary<string, Color>
{
    public StringColorDictionary() {}
    public StringColorDictionary(IDictionary<string, Color> dict) : base(dict) {}
}
```

### 5. Encapsulate with a property

```csharp
[SerializeField]
MyScriptColorDictionary m_myDictionary;

public IDictionary<MyScript, Color> MyDictionary
{
    get { return m_myDictionary; }
    set { m_myDictionary.CopyFrom(value); }
}
```

## Configuration

No runtime configuration is needed. The property drawer is automatically registered via `[CustomPropertyDrawer]` attributes:

| Drawer Class | Targets | Attribute |
|---|---|---|
| `SerializableDictionaryPropertyDrawer` | `SerializableDictionaryBase` and all subclasses | `[CustomPropertyDrawer(typeof(SerializableDictionaryBase), true)]` |
| `SerializableDictionaryPropertyDrawer` | `SerializableHashSetBase` and all subclasses | `[CustomPropertyDrawer(typeof(SerializableHashSetBase), true)]` |
| `SerializableDictionaryStoragePropertyDrawer` | `SerializableDictionaryBase.Storage` and all subclasses | `[CustomPropertyDrawer(typeof(SerializableDictionaryBase.Storage), true)]` |

## Usage Examples

### Custom serializable class as value

```csharp
[Serializable]
public class MyClass
{
    public int i;
    public string str;
}

[Serializable]
public class StringMyClassDictionary : SerializableDictionary<string, MyClass> {}

public class Demo : MonoBehaviour
{
    public StringMyClassDictionary m_data;
}
```

### Multiple dictionaries in one script

```csharp
public class MultiDemo : MonoBehaviour
{
    public StringStringDictionary m_names;

    [SerializeField]
    MyScriptColorDictionary m_colors;
    public IDictionary<MyScript, Color> Colors
    {
        get { return m_colors; }
        set { m_colors.CopyFrom(value); }
    }

    public StringMyClassDictionary m_configs;
}
```

## Operational Checks

| Check | How to Verify | Expected Result |
|---|---|---|
| Dictionary appears in Inspector | Add a `SerializableDictionary` subclass field to a MonoBehaviour and select the object | Foldout with key/value entries and +/- buttons is displayed |
| HashSet appears in Inspector | Add a `SerializableHashSet` subclass field to a MonoBehaviour and select the object | Foldout with value entries and +/- buttons is displayed |
| Add entry | Click the **+** button while the foldout is expanded | A new default key/value row is appended |
| Remove entry | Click the **-** button next to an entry | The entry is removed from the list |
| Duplicate key warning | Enter two entries with the same key | Warning icon with tooltip "Conflicting key, this entry will be lost" appears on the duplicate row |
| Null key warning | Leave a key field as null (for reference types) | Warning icon with tooltip "Null key, this entry will be lost" appears |
| Serialization round-trip | Enter data in Play mode, exit Play mode, re-enter | Inspector values persist across domain reloads (within normal Unity serialization rules) |
| CopyFrom works | Call `CopyFrom` from script with a populated `Dictionary` | All entries are replaced with the source dictionary's contents |
| Dictionary-of-lists | Use the 3-argument variant with a `Storage` wrapper | List values are accessible directly (no `.data` access needed) |

## Flow Diagram

```
                    ┌──────────────────────────┐
                    │   Unity Serialization     │
                    │      Pipeline             │
                    └────────┬─────────────────┘
                             │
              ┌──────────────┴──────────────┐
              ▼                             ▼
   ┌─────────────────────┐      ┌─────────────────────┐
   │  OnBeforeSerialize() │      │  OnAfterDeserialize()│
   │                     │      │                     │
   │ Flatten Dictionary   │      │ Rebuild Dictionary   │
   │ into m_keys[] and    │      │ from m_keys[] and    │
   │ m_values[] arrays    │      │ m_values[] arrays    │
   └─────────┬───────────┘      └─────────┬───────────┘
             │                             │
             ▼                             ▼
   ┌─────────────────────┐      ┌─────────────────────┐
   │  TKey[] m_keys       │      │  Dictionary<TKey,   │
   │  TValueStorage[]     │◄────►│    TValue> m_dict   │
   │    m_values          │      │  (runtime usage)    │
   └─────────────────────┘      └─────────────────────┘
   [SerializeField] arrays       Internal dictionary

              ┌──────────────────────────────────┐
              │   Inspector (Editor Only)         │
              │                                  │
              │ SerializableDictionaryProperty-   │
              │   Drawer reads m_keys/m_values   │
              │                                  │
              │ ┌─ Detect duplicate keys ──► ⚠   │
              │ ├─ Detect null keys ────────► ⚠  │
              │ ├─ Add entry (+) button          │
              │ └─ Remove entry (-) button       │
              └──────────────────────────────────┘
```

## Project Structure

```
Assets/Scripts/Shared/Implementation/Tools/SerializableDictionary/
├── README.md                              # This documentation
├── LICENSE                                # MIT License (Mathieu Le Ber, 2017)
├── SerializableDictionary.cs              # Core runtime classes
│   ├── SerializableDictionaryBase             (abstract, non-generic root with nested Storage / Dictionary)
│   ├── SerializableDictionaryBase<TKey, TValue, TValueStorage>
│   │       (abstract generic base implementing IDictionary<TKey,TValue>,
│   │        IDictionary, ISerializationCallbackReceiver,
│   │        IDeserializationCallback, ISerializable)
│   ├── SerializableDictionary                 (static helper with Storage<T> nested class)
│   ├── SerializableDictionary<TKey, TValue>   (concrete 2-param dictionary)
│   └── SerializableDictionary<TKey, TValue, TValueStorage>
│                                              (concrete 3-param dictionary for list/array values)
├── SerializableHashSet.cs                 # Serializable hash-set
│   ├── SerializableHashSetBase                (abstract, non-generic root)
│   └── SerializableHashSet<T>                 (concrete generic set implementing ISet<T>,
│                                               ISerializationCallbackReceiver,
│                                               IDeserializationCallback, ISerializable)
└── Editor/
    └── SerializableDictionaryPropertyDrawer.cs   # Custom Inspector drawers
        ├── SerializableDictionaryPropertyDrawer   (draws both dictionaries and hash-sets)
        └── SerializableDictionaryStoragePropertyDrawer (unwraps Storage<T> for clean display)
```

### Inheritance Hierarchy

```
SerializableDictionaryBase                         (abstract)
└── SerializableDictionaryBase<TKey,TValue,TValueStorage>  (abstract, IDictionary<K,V>, IDictionary, etc.)
    ├── SerializableDictionary<TKey,TValue>         (concrete, simple key/value)
    └── SerializableDictionary<TKey,TValue,TValueStorage>  (concrete, list/array values)

SerializableHashSetBase                            (abstract)
└── SerializableHashSet<T>                         (concrete, ISet<T>, etc.)

PropertyDrawer (UnityEditor)
├── SerializableDictionaryPropertyDrawer           (draws dictionaries + hash-sets)
└── SerializableDictionaryStoragePropertyDrawer    (draws Storage<T> wrappers)
```

## License

This module includes code originally released under the **MIT License** by Mathieu Le Ber (2017). It is redistributed as part of the FishMMO project under the terms of the FishMMO project license. See the [LICENSE](LICENSE) file in this directory for the original MIT license text.
