# Node

## [ Slot Layout Behavior ]

Nodes mount connectors; this set of additional properties is responsible for the behavior of these connectors.

> **Attribute Overview**

| Attribute | Type | Description |
| --- | --- | --- |
| IsEnabled | bool | Toggle |
| SlotNames | string[] | single Slot, Slot enumerator |
| CoordinateHostName | string | Slot needs to be laid out in a host container. |

## [ Node Drag Behavior ]

Handle node drag behavior

> **Attribute Overview**

| Additional Attributes | Type | Function |
| --- | --- | --- |
| IsEnabled | bool | Switch |
| CoordinateHostName | string | Drag will perform offset coordinate detection based on a host container. |