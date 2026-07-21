# Template Selector

Inherits `DataTemplateSelector`. Maps ViewModel types to corresponding DataTemplates.

## Usage

```shell
dotnet new wpf-v-selector -n MyTemplateSelector -ns MyApp.Views
dotnet new ava-v-selector -n MyTemplateSelector -ns MyApp.Views
dotnet new winui-v-selector -n MyTemplateSelector -ns MyApp.Views
dotnet new maui-v-selector -n MyTemplateSelector -ns MyApp.Views
```

## Parameters

Common parameters only — no visual customization.

| Short | Parameter | Default | Description |
|-------|-----------|---------|-------------|
| `-n` | `--name` | `TemplateSelector` | Class/filename |
| `-o` | `--output` | Current dir | Output directory |
| | `--namespace` | `MyApp.Views` | Namespace |
