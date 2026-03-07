# Codex Handoff — Speakly Profiles Page Redesign
> Target: WPF / C# project "Speakly"  
> Scope: Profiles settings page only  
> Reference HTML mockup: `speakly-profiles.html` (provided separately)

---

## 1. Goal

Replace the current flat, hard-to-scan Profiles page with a **card-grouped layout** that makes the hierarchy obvious:

1. **Active Profile** — selector + live summary tiles  
2. **Profile Settings** — rename, process names, app-detection row  
3. **Create New Profile** — visually distinct "add" card  
4. **Danger Zone** — isolated delete section with warning copy  

---

## 2. Design Tokens (Resource Dictionary)

Create or extend `Resources/Theme.xaml` with these entries. All colours below are `#AARRGGBB`.

```xml
<!-- Backgrounds -->
<Color x:Key="BgColor">#FF0F1117</Color>
<Color x:Key="SurfaceColor">#FF181C25</Color>
<Color x:Key="CardColor">#FF1E2330</Color>

<!-- Borders -->
<Color x:Key="BorderColor">#FF2A3045</Color>
<Color x:Key="BorderHighColor">#FF3A4560</Color>

<!-- Text -->
<Color x:Key="TextColor">#FFD4DAF0</Color>
<Color x:Key="MutedColor">#FF6B7599</Color>

<!-- Accents -->
<Color x:Key="AccentColor">#FF4F7CFF</Color>
<Color x:Key="AccentHiColor">#FF7A9DFF</Color>
<Color x:Key="DangerColor">#FFFF5A72</Color>
<Color x:Key="GreenColor">#FF3DDC97</Color>

<!-- Derived SolidColorBrushes (one per Color above, suffix "Brush") -->
<!-- e.g. <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}"/> -->
```

Also add:
```xml
<CornerRadius x:Key="RadiusLg">12</CornerRadius>
<CornerRadius x:Key="RadiusSm">8</CornerRadius>
<Thickness  x:Key="CardPadding">20</Thickness>
```

---

## 3. New / Modified Files

### 3.1 `Views/ProfilesView.xaml`  ← **main change**

Replace the existing content with the structure below. Use a `ScrollViewer` > `StackPanel` (spacing 12) containing four `Border` cards.

#### Overall skeleton

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto"
              Background="{StaticResource BgBrush}">
  <StackPanel Margin="32" MaxWidth="540">

    <!-- Page Header -->
    <!-- Card A: Active Profile -->
    <!-- Card B: Profile Settings -->
    <!-- Card C: Create New Profile -->
    <!-- Card D: Danger Zone -->

  </StackPanel>
</ScrollViewer>
```

---

#### Page Header

```xml
<StackPanel Orientation="Horizontal" Margin="0,0,0,28">
  <!-- Gradient icon pill -->
  <Border Width="36" Height="36" CornerRadius="10">
    <Border.Background>
      <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
        <GradientStop Color="{StaticResource AccentColor}" Offset="0"/>
        <GradientStop Color="#FF7B5CFA" Offset="1"/>
      </LinearGradientBrush>
    </Border.Background>
    <TextBlock Text="🎙" FontSize="16" HorizontalAlignment="Center"
               VerticalAlignment="Center"/>
  </Border>
  <StackPanel Margin="12,0,0,0" VerticalAlignment="Center">
    <TextBlock Text="Profiles" FontSize="20" FontWeight="SemiBold"
               Foreground="{StaticResource TextBrush}"/>
    <TextBlock Text="Map STT configurations to specific applications"
               FontSize="12" Foreground="{StaticResource MutedBrush}"/>
  </StackPanel>
</StackPanel>
```

---

#### Card A — Active Profile

```xml
<Border Style="{StaticResource CardStyle}">
  <StackPanel>
    <TextBlock Text="ACTIVE PROFILE" Style="{StaticResource CardLabelStyle}"/>

    <!-- Selector row -->
    <Grid Margin="0,0,0,16">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <ComboBox Grid.Column="0"
                ItemsSource="{Binding Profiles}"
                SelectedItem="{Binding SelectedProfile}"
                Style="{StaticResource DarkComboStyle}"/>
      <!-- Active badge -->
      <Border Grid.Column="1" Margin="10,0,0,0"
              Background="#1A3DDC97" BorderBrush="#403DDC97"
              BorderThickness="1" CornerRadius="20" Padding="10,4">
        <StackPanel Orientation="Horizontal">
          <Ellipse Width="6" Height="6" Fill="{StaticResource GreenBrush}"
                   Margin="0,0,6,0">
            <!-- Pulse: trigger via DoubleAnimation on Opacity -->
          </Ellipse>
          <TextBlock Text="Active" FontSize="11" FontWeight="Medium"
                     Foreground="{StaticResource GreenBrush}"/>
        </StackPanel>
      </Border>
    </Grid>

    <!-- Summary 2-col UniformGrid -->
    <UniformGrid Columns="2" >
      <!-- Repeat SummaryTile (see §3.3) for: STT Engine, Refinement,
           Language, Clipboard+Failover, Dictionary+Mapped (ColSpan via wrapper) -->
    </UniformGrid>
  </StackPanel>
</Border>
```

**SummaryTile reusable template** — define as a `DataTemplate` or `UserControl`:

```xml
<!-- Each tile: -->
<Border Margin="0,0,8,8" Background="{StaticResource SurfaceBrush}"
        BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
        CornerRadius="{StaticResource RadiusSm}" Padding="12,10">
  <StackPanel>
    <TextBlock Text="{Binding Key}" Style="{StaticResource TileLabelStyle}"/>
    <TextBlock Text="{Binding Value}" Style="{StaticResource TileValueStyle}"/>
  </StackPanel>
</Border>
```

For the last "full-width" tile, wrap in a `<Grid ColumnSpan="2">` or use a dedicated `Border` after the `UniformGrid`.

---

#### Card B — Profile Settings

```xml
<Border Style="{StaticResource CardStyle}">
  <StackPanel>
    <TextBlock Text="PROFILE SETTINGS" Style="{StaticResource CardLabelStyle}"/>

    <!-- Rename row -->
    <TextBlock Text="Profile name" Style="{StaticResource FormLabelStyle}"/>
    <Grid Margin="0,4,0,14">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBox Grid.Column="0" Text="{Binding ProfileName, UpdateSourceTrigger=PropertyChanged}"
               Style="{StaticResource DarkTextBoxStyle}"/>
      <Button Grid.Column="1" Content="Rename" Margin="8,0,0,0"
              Command="{Binding RenameCommand}"
              Style="{StaticResource GhostButtonStyle}"/>
    </Grid>

    <!-- Process names -->
    <TextBlock Text="Process names" Style="{StaticResource FormLabelStyle}"/>
    <TextBox Margin="0,4,0,4" MinHeight="72" AcceptsReturn="True"
             Text="{Binding ProcessNames, UpdateSourceTrigger=PropertyChanged}"
             Style="{StaticResource DarkTextBoxStyle}" TextWrapping="Wrap"
             VerticalScrollBarVisibility="Auto"/>
    <TextBlock Style="{StaticResource HintStyle}"
               Text="One name per line or comma-separated. Use process name only — e.g. code, chrome, notepad."/>

    <!-- App detection info row -->
    <Border Margin="0,8,0,0"
            Background="{StaticResource SurfaceBrush}"
            BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
            CornerRadius="{StaticResource RadiusSm}" Padding="14,10">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <StackPanel Grid.Column="0">
          <TextBlock Foreground="{StaticResource MutedBrush}" FontSize="12">
            <Run Text="Current app: "/>
            <Run Text="{Binding CurrentApp}" FontWeight="SemiBold"
                 Foreground="{StaticResource TextBrush}"/>
          </TextBlock>
          <TextBlock Foreground="{StaticResource MutedBrush}" FontSize="12">
            <Run Text="Matched profile: "/>
            <Run Text="{Binding MatchedProfile}"
                 Foreground="{StaticResource AccentHiBrush}" FontWeight="Medium"/>
          </TextBlock>
        </StackPanel>
        <StackPanel Grid.Column="1" Orientation="Horizontal">
          <Button Content="Use Current App" Command="{Binding UseCurrentAppCommand}"
                  Style="{StaticResource GhostButtonStyle}" Margin="0,0,6,0"/>
          <Button Content="Refresh" Command="{Binding RefreshMatchCommand}"
                  Style="{StaticResource GhostButtonStyle}"/>
        </StackPanel>
      </Grid>
    </Border>

    <!-- Divider -->
    <Separator Margin="0,16" Background="{StaticResource BorderBrush}"/>

    <!-- Save / Next -->
    <StackPanel Orientation="Horizontal">
      <Button Content="💾  Save mappings" Command="{Binding SaveCommand}"
              Style="{StaticResource PrimaryButtonStyle}" Margin="0,0,8,0"/>
      <Button Content="Next profile →" Command="{Binding NextProfileCommand}"
              Style="{StaticResource GhostButtonStyle}"/>
    </StackPanel>
  </StackPanel>
</Border>
```

---

#### Card C — Create New Profile

```xml
<!-- Dashed border trick: use a Rectangle with StrokeDashArray as background -->
<Border CornerRadius="{StaticResource RadiusLg}" Padding="20"
        Background="#083F7CFF">
  <Border.BorderBrush>
    <SolidColorBrush Color="{StaticResource BorderColor}"/>
  </Border.BorderBrush>
  <!-- WPF doesn't support dashed BorderBrush natively;
       use a DrawingBrush or place a Rectangle inside a Grid as a workaround: -->
  <Grid>
    <Rectangle RadiusX="12" RadiusY="12" StrokeThickness="1.5"
               StrokeDashArray="6,3">
      <Rectangle.Stroke>
        <SolidColorBrush Color="{StaticResource BorderColor}"/>
      </Rectangle.Stroke>
    </Rectangle>
    <StackPanel Margin="0,0,0,0">
      <TextBlock Text="CREATE NEW PROFILE" Style="{StaticResource CardLabelStyle}"/>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBox Grid.Column="0"
                 Text="{Binding NewProfileName, UpdateSourceTrigger=PropertyChanged}"
                 Style="{StaticResource DarkTextBoxStyle}"
                 materialDesign:HintAssist.Hint="e.g. Chrome, VS Code…"/>
        <Button Grid.Column="1" Content="＋  Create profile"
                Command="{Binding CreateProfileCommand}"
                Style="{StaticResource PrimaryButtonStyle}" Margin="8,0,0,0"/>
      </Grid>
    </StackPanel>
  </Grid>
</Border>
```

---

#### Card D — Danger Zone

```xml
<Border CornerRadius="{StaticResource RadiusLg}" Padding="20"
        Background="#0AFF5A72"
        BorderBrush="#33FF5A72" BorderThickness="1">
  <StackPanel>
    <TextBlock Text="⚠  DANGER ZONE" Style="{StaticResource CardLabelStyle}"
               Foreground="{StaticResource DangerBrush}"/>
    <TextBlock TextWrapping="Wrap" FontSize="12" Margin="0,0,0,12"
               Foreground="{StaticResource MutedBrush}"
               Text="Deleting this profile will permanently remove its settings and all process name mappings. This action cannot be undone."/>
    <Button Command="{Binding DeleteProfileCommand}"
            Style="{StaticResource DangerButtonStyle}">
      <Button.Content>
        <TextBlock>
          <Run Text="Delete "/>
          <Run Text="{Binding SelectedProfile.Name, StringFormat='&quot;{0}&quot;'}"/>
          <Run Text=" profile"/>
        </TextBlock>
      </Button.Content>
    </Button>
  </StackPanel>
</Border>
```

---

### 3.2 `Styles/ProfilesStyles.xaml`  ← **new file, merge into App.xaml**

Define all the named styles referenced above:

| Key | Type | Notes |
|-----|------|-------|
| `CardStyle` | `Style<Border>` | `Background=CardBrush`, `BorderBrush=BorderBrush`, `BorderThickness=1`, `CornerRadius=RadiusLg`, `Padding=CardPadding`, `Margin="0,0,0,12"` — add `Trigger` on `IsMouseOver` to animate `BorderBrush → BorderHighBrush` |
| `CardLabelStyle` | `Style<TextBlock>` | `FontSize=10`, `FontWeight=SemiBold`, `CharacterSpacing=120` (≈letter-spacing), `Foreground=MutedBrush`, `Margin="0,0,0,14"` |
| `FormLabelStyle` | `Style<TextBlock>` | `FontSize=12`, `FontWeight=Medium`, `Foreground=MutedBrush` |
| `HintStyle` | `Style<TextBlock>` | `FontSize=11`, `Foreground=MutedBrush`, `TextWrapping=Wrap`, `LineHeight=18` |
| `TileLabelStyle` | `Style<TextBlock>` | `FontSize=10`, `Foreground=MutedBrush`, `Margin="0,0,0,3"` |
| `TileValueStyle` | `Style<TextBlock>` | `FontSize=13`, `FontWeight=Medium`, `FontFamily=Consolas` |
| `DarkTextBoxStyle` | `Style<TextBox>` | `Background=SurfaceBrush`, `BorderBrush=BorderBrush`, `Foreground=TextBrush`, `FontFamily=Consolas`, `FontSize=13`, `Padding="9,8"`, `CornerRadius=RadiusSm` — focus trigger → `BorderBrush=AccentBrush` + glow `Effect` |
| `DarkComboStyle` | `Style<ComboBox>` | Match TextBox appearance |
| `PrimaryButtonStyle` | `Style<Button>` | `Background=AccentBrush`, `Foreground=White`, `CornerRadius=RadiusSm`, `Padding="16,8"`, `FontSize=13` — hover: `Opacity=0.88` |
| `GhostButtonStyle` | `Style<Button>` | `Background=SurfaceBrush`, `BorderBrush=BorderBrush`, `BorderThickness=1`, `Foreground=TextBrush`, `CornerRadius=RadiusSm`, `Padding="14,8"` |
| `DangerButtonStyle` | `Style<Button>` | `Background=#1AFF5A72`, `BorderBrush=#33FF5A72`, `BorderThickness=1`, `Foreground=DangerBrush`, `CornerRadius=RadiusSm`, `Padding="14,8"` |

---

### 3.3 `ViewModels/ProfilesViewModel.cs`  ← **likely already exists, extend it**

Ensure these properties / commands are present:

```csharp
// Properties
ObservableCollection<ProfileModel> Profiles { get; }
ProfileModel SelectedProfile { get; set; }   // drives all Card A summary tiles
string ProfileName { get; set; }
string ProcessNames { get; set; }            // newline-joined list
string CurrentApp { get; }                   // updated by RefreshMatch
string MatchedProfile { get; }
string NewProfileName { get; set; }

// Summary tile helpers (auto-computed from SelectedProfile)
string SttEngine   => SelectedProfile?.SttProvider + " / " + SelectedProfile?.SttModel;
string Refinement  => SelectedProfile?.RefinementProvider + " / " + SelectedProfile?.RefinementModel;
string Language    => SelectedProfile?.Language;
string ClipboardInfo => $"Clipboard: {(SelectedProfile?.ClipboardEnabled == true ? "On" : "Off")} · Failover: ...";
string MappingInfo => $"{SelectedProfile?.DictionaryTermCount} terms · {SelectedProfile?.MappedApps.Count} apps";

// Commands
ICommand RenameCommand        { get; }   // validates non-empty, updates model
ICommand SaveCommand          { get; }   // persists ProcessNames mapping
ICommand NextProfileCommand   { get; }   // cycles SelectedProfile
ICommand UseCurrentAppCommand { get; }   // appends CurrentApp to ProcessNames
ICommand RefreshMatchCommand  { get; }   // re-detects foreground window → CurrentApp + MatchedProfile
ICommand CreateProfileCommand { get; }   // creates new profile from NewProfileName, clears field
ICommand DeleteProfileCommand { get; }   // shows confirmation dialog, then removes SelectedProfile
```

**Confirmation dialog for Delete** — use an existing dialog service or a simple WPF `MessageBox`:
```csharp
var result = MessageBox.Show(
    $"Delete profile \"{SelectedProfile.Name}\"? This cannot be undone.",
    "Delete Profile", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
if (result == MessageBoxResult.OK) { /* remove and persist */ }
```

---

### 3.4 Pulse animation on the Active badge

In `ProfilesView.xaml` code-behind or via a `Trigger`, animate the green dot's `Opacity`:

```xml
<Ellipse.Triggers>
  <EventTrigger RoutedEvent="Loaded">
    <BeginStoryboard>
      <Storyboard RepeatBehavior="Forever">
        <DoubleAnimation Storyboard.TargetProperty="Opacity"
                         From="1" To="0.35" Duration="0:0:1"
                         AutoReverse="True"/>
      </Storyboard>
    </BeginStoryboard>
  </EventTrigger>
</Ellipse.Triggers>
```

---

## 4. Layout Rules (constraints for Codex)

- `MaxWidth="540"` on the root `StackPanel` — centre it in wider windows.
- All cards have `Margin="0,0,0,12"` bottom spacing (except last card → `0`).
- `UniformGrid` for summary tiles uses `Columns="2"` and each tile has `Margin="0,0,8,8"`.
- The last "full-width" tile (Dictionary · Mapped) sits outside the `UniformGrid` with `Margin="0,0,0,0"`.
- Buttons in action rows use `HorizontalAlignment="Left"` — never stretch.
- No bold or headers inside running-text labels — use the `MutedBrush` + small size to create hierarchy instead.

---

## 5. What NOT to Change

- Do **not** alter `ProfileModel`, the underlying persistence layer, or how process-name matching works.
- Do **not** change any other views or navigation structure.
- Do **not** introduce new NuGet packages unless the dashed-border workaround requires a helper — if so, prefer a pure-XAML `DrawingBrush` solution over a library.

---

## 6. Acceptance Checklist

- [ ] Four cards render in correct order with correct background/border colours
- [ ] `SelectedProfile` change updates all summary tiles instantly
- [ ] Rename renames in place and reflects in the ComboBox
- [ ] `Use Current App` appends correctly and does not duplicate
- [ ] `Refresh Match` updates `CurrentApp` and `MatchedProfile` labels
- [ ] `Create profile` clears the input and selects the new profile
- [ ] `Delete profile` shows confirmation and removes only the selected profile
- [ ] No horizontal scrollbar appears at `MaxWidth=540`
- [ ] Focus ring (blue glow) visible on all text inputs
- [ ] Pulse animation plays on the green Active dot
