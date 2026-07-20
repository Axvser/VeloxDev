# Theme System Architecture

The theme system provides dynamic Dark/Light switching with platform integration.

## Core Types

| Type | Role |
|------|------|
| `ThemeConfigAttribute` | Declares which properties to swap per theme |
| `ThemeManager` | Singleton that manages active theme and broadcasts changes |
| `ObjectConverter` | Converts between theme-specific resource dictionaries |

## Platform Integration

On Desktop, the theme manager subscribes to `PlatformColorValues.ColorValuesChanged` and automatically switches when the OS theme changes.
