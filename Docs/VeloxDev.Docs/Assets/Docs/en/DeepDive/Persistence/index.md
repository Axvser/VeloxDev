# Persistence Architecture

Serialization and deserialization of workflow state.

## Format

The serializer produces a JSON graph compatible with MAF's `ISerialization` contract. It preserves:

- Node hierarchy (Tree → Nodes → Slots → Links)
- Property values (including custom business data)
- Visual state (position, size, zoom level)
- Agent scope configuration

## Cross-Platform

The same serialized JSON can be loaded on Desktop, Browser, or Mobile — enabling cloud-backed workflow persistence.
