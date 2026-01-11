# PlatypusAI - AI Image Generator
# A WPF application for generating AI images from text prompts
# Supports OpenAI DALL-E and Stability AI

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ============================================
# Configuration
# ============================================
$script:BasePath = "C:\ProgramFiles\PlatypusUtils"
$script:ConfigPath = Join-Path $script:BasePath "Data\PlatypusAI_Config.json"
$script:OutputPath = Join-Path $script:BasePath "AI_Images"
$script:LogPath = Join-Path $script:BasePath "Logs"

# Ensure directories exist
foreach ($dir in @($script:BasePath, (Join-Path $script:BasePath "Data"), $script:OutputPath, $script:LogPath)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
}

# ============================================
# Logging
# ============================================
function Write-AILog {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logFile = Join-Path $script:LogPath "PlatypusAI_$(Get-Date -Format 'yyyy-MM-dd').log"
    "$timestamp [$Level] $Message" | Out-File -FilePath $logFile -Append -Encoding UTF8
}

# ============================================
# Configuration Management
# ============================================
$script:Config = @{
    OpenAI_ApiKey = ""
    StabilityAI_ApiKey = ""
    AzureOpenAI_ApiKey = ""
    AzureOpenAI_Endpoint = ""
    AzureOpenAI_DeploymentName = "dall-e-3"
    DefaultProvider = "OpenAI"
    DefaultSize = "1024x1024"
    DefaultQuality = "standard"
    DefaultStyle = "vivid"
    OutputFolder = $script:OutputPath
    Theme = "Dark"
}

function Load-Config {
    if (Test-Path $script:ConfigPath) {
        try {
            $loaded = Get-Content $script:ConfigPath -Raw | ConvertFrom-Json
            foreach ($prop in $loaded.PSObject.Properties) {
                if ($script:Config.ContainsKey($prop.Name)) {
                    $script:Config[$prop.Name] = $prop.Value
                }
            }
            Write-AILog "Configuration loaded"
        } catch {
            Write-AILog "Failed to load config: $_" "ERROR"
        }
    }
}

function Save-Config {
    try {
        $script:Config | ConvertTo-Json -Depth 5 | Set-Content $script:ConfigPath -Encoding UTF8
        Write-AILog "Configuration saved"
    } catch {
        Write-AILog "Failed to save config: $_" "ERROR"
    }
}

Load-Config

# ============================================
# API Functions
# ============================================
function Invoke-OpenAI-ImageGeneration {
    param(
        [string]$Prompt,
        [string]$Size = "1024x1024",
        [string]$Quality = "standard",
        [string]$Style = "vivid",
        [int]$Count = 1
    )
    
    $apiKey = $script:Config.OpenAI_ApiKey
    if (-not $apiKey) {
        throw "OpenAI API key not configured. Please set your API key in Settings."
    }
    
    $headers = @{
        "Authorization" = "Bearer $apiKey"
        "Content-Type" = "application/json"
    }
    
    $body = @{
        model = "dall-e-3"
        prompt = $Prompt
        n = 1  # DALL-E 3 only supports 1 image at a time
        size = $Size
        quality = $Quality
        style = $Style
        response_format = "url"
    } | ConvertTo-Json
    
    Write-AILog "Sending request to OpenAI DALL-E 3..."
    
    $response = Invoke-RestMethod -Uri "https://api.openai.com/v1/images/generations" `
        -Method Post -Headers $headers -Body $body -TimeoutSec 120
    
    return $response.data
}

function Invoke-StabilityAI-ImageGeneration {
    param(
        [string]$Prompt,
        [string]$Size = "1024x1024",
        [int]$Steps = 30,
        [int]$Count = 1
    )
    
    $apiKey = $script:Config.StabilityAI_ApiKey
    if (-not $apiKey) {
        throw "Stability AI API key not configured. Please set your API key in Settings."
    }
    
    # Parse size
    $dims = $Size -split 'x'
    $width = [int]$dims[0]
    $height = [int]$dims[1]
    
    $headers = @{
        "Authorization" = "Bearer $apiKey"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }
    
    $body = @{
        text_prompts = @(
            @{
                text = $Prompt
                weight = 1
            }
        )
        cfg_scale = 7
        height = $height
        width = $width
        steps = $Steps
        samples = $Count
    } | ConvertTo-Json -Depth 5
    
    Write-AILog "Sending request to Stability AI..."
    
    $response = Invoke-RestMethod -Uri "https://api.stability.ai/v1/generation/stable-diffusion-xl-1024-v1-0/text-to-image" `
        -Method Post -Headers $headers -Body $body -TimeoutSec 120
    
    return $response.artifacts
}

function Download-Image {
    param(
        [string]$Url,
        [string]$OutputPath
    )
    
    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($Url, $OutputPath)
        return $true
    } catch {
        Write-AILog "Failed to download image: $_" "ERROR"
        return $false
    }
}

function Save-Base64Image {
    param(
        [string]$Base64Data,
        [string]$OutputPath
    )
    
    try {
        $bytes = [Convert]::FromBase64String($Base64Data)
        [System.IO.File]::WriteAllBytes($OutputPath, $bytes)
        return $true
    } catch {
        Write-AILog "Failed to save base64 image: $_" "ERROR"
        return $false
    }
}

# ============================================
# XAML GUI Definition
# ============================================
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="PlatypusAI - AI Image Generator" 
        Height="800" Width="1100"
        MinHeight="600" MinWidth="800"
        WindowStartupLocation="CenterScreen"
        Background="#1E1E1E">
    
    <Window.Resources>
        <!-- Dark Theme Colors -->
        <SolidColorBrush x:Key="PrimaryBg" Color="#1E1E1E"/>
        <SolidColorBrush x:Key="SecondaryBg" Color="#252526"/>
        <SolidColorBrush x:Key="AccentColor" Color="#007ACC"/>
        <SolidColorBrush x:Key="TextColor" Color="#CCCCCC"/>
        <SolidColorBrush x:Key="BorderColor" Color="#3F3F46"/>
        
        <!-- Button Style -->
        <Style x:Key="ModernButton" TargetType="Button">
            <Setter Property="Background" Value="#007ACC"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="20,10"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" 
                                CornerRadius="4" 
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="#1C97EA"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter Property="Background" Value="#005A9E"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Background" Value="#4D4D4D"/>
                                <Setter Property="Foreground" Value="#808080"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        
        <!-- Secondary Button Style -->
        <Style x:Key="SecondaryButton" TargetType="Button" BasedOn="{StaticResource ModernButton}">
            <Setter Property="Background" Value="#3E3E42"/>
        </Style>
        
        <!-- TextBox Style -->
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#2D2D30"/>
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="8,6"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>
        
        <!-- ComboBox Style -->
        <Style TargetType="ComboBox">
            <Setter Property="Background" Value="#2D2D30"/>
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="Padding" Value="8,6"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>
        
        <!-- Label Style -->
        <Style TargetType="Label">
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>
        
        <!-- GroupBox Style -->
        <Style TargetType="GroupBox">
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="Margin" Value="5"/>
            <Setter Property="Padding" Value="10"/>
        </Style>
    </Window.Resources>
    
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- Header with Gradient -->
        <Border Grid.Row="0" Height="80">
            <Border.Background>
                <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                    <GradientStop Color="#1a5276" Offset="0"/>
                    <GradientStop Color="#2ecc71" Offset="1"/>
                </LinearGradientBrush>
            </Border.Background>
            <Grid>
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="20,0">
                    <TextBlock Text="[AI]" FontSize="32" FontWeight="Bold" VerticalAlignment="Center" Margin="0,0,15,0" Foreground="White"/>
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="PlatypusAI" FontSize="28" FontWeight="Bold" Foreground="White"/>
                        <TextBlock Text="AI Image Generator" FontSize="14" Foreground="#DDDDDD"/>
                    </StackPanel>
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="20,0">
                    <Button x:Name="BtnSettings" Content="Settings" Style="{StaticResource SecondaryButton}" Margin="5,0"/>
                    <Button x:Name="BtnHelp" Content="Help" Style="{StaticResource SecondaryButton}" Margin="5,0"/>
                </StackPanel>
            </Grid>
        </Border>
        
        <!-- Main Content -->
        <Grid Grid.Row="1" Margin="20">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="350"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            
            <!-- Left Panel - Controls -->
            <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto">
                <StackPanel>
                    <!-- Prompt Input -->
                    <GroupBox Header="Prompt" Margin="0,0,10,10">
                        <StackPanel>
                            <TextBox x:Name="TxtPrompt" 
                                     Height="120" 
                                     TextWrapping="Wrap" 
                                     AcceptsReturn="True"
                                     VerticalScrollBarVisibility="Auto"
                                     Text="A majestic platypus wearing a top hat, steampunk style, detailed digital art"/>
                            <TextBlock Text="Describe the image you want to generate" 
                                       Foreground="#808080" FontSize="11" Margin="0,5,0,0"/>
                        </StackPanel>
                    </GroupBox>
                    
                    <!-- Provider Selection -->
                    <GroupBox Header="AI Provider" Margin="0,0,10,10">
                        <StackPanel>
                            <ComboBox x:Name="CmbProvider" SelectedIndex="0">
                                <ComboBoxItem Content="OpenAI DALL-E 3"/>
                                <ComboBoxItem Content="Microsoft Copilot (Azure)"/>
                                <ComboBoxItem Content="Stability AI (SDXL)"/>
                            </ComboBox>
                            <TextBlock x:Name="TxtProviderInfo" 
                                       Text="Using DALL-E 3 - Best quality, $0.04-0.12 per image" 
                                       Foreground="#808080" FontSize="11" Margin="0,5,0,0" TextWrapping="Wrap"/>
                        </StackPanel>
                    </GroupBox>
                    
                    <!-- Image Options -->
                    <GroupBox Header="Image Options" Margin="0,0,10,10">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            
                            <Label Content="Size:" Grid.Row="0" Grid.Column="0" VerticalAlignment="Center"/>
                            <ComboBox x:Name="CmbSize" Grid.Row="0" Grid.Column="1" Margin="5">
                                <ComboBoxItem Content="1024x1024" IsSelected="True"/>
                                <ComboBoxItem Content="1792x1024"/>
                                <ComboBoxItem Content="1024x1792"/>
                                <ComboBoxItem Content="512x512"/>
                            </ComboBox>
                            
                            <Label Content="Quality:" Grid.Row="1" Grid.Column="0" VerticalAlignment="Center"/>
                            <ComboBox x:Name="CmbQuality" Grid.Row="1" Grid.Column="1" Margin="5">
                                <ComboBoxItem Content="standard" IsSelected="True"/>
                                <ComboBoxItem Content="hd"/>
                            </ComboBox>
                            
                            <Label Content="Style:" Grid.Row="2" Grid.Column="0" VerticalAlignment="Center"/>
                            <ComboBox x:Name="CmbStyle" Grid.Row="2" Grid.Column="1" Margin="5">
                                <ComboBoxItem Content="vivid" IsSelected="True"/>
                                <ComboBoxItem Content="natural"/>
                            </ComboBox>
                            
                            <Label Content="Count:" Grid.Row="3" Grid.Column="0" VerticalAlignment="Center"/>
                            <ComboBox x:Name="CmbCount" Grid.Row="3" Grid.Column="1" Margin="5">
                                <ComboBoxItem Content="1" IsSelected="True"/>
                                <ComboBoxItem Content="2"/>
                                <ComboBoxItem Content="3"/>
                                <ComboBoxItem Content="4"/>
                            </ComboBox>
                        </Grid>
                    </GroupBox>
                    
                    <!-- Generate Button -->
                    <Button x:Name="BtnGenerate" 
                            Content="Generate Image" 
                            Style="{StaticResource ModernButton}"
                            FontSize="16"
                            Height="50"
                            Margin="0,10,10,10"/>
                    
                    <!-- Progress -->
                    <StackPanel x:Name="PnlProgress" Visibility="Collapsed" Margin="0,0,10,10">
                        <ProgressBar x:Name="PrgGenerate" Height="20" IsIndeterminate="True"/>
                        <TextBlock x:Name="TxtProgress" Text="Generating image..." 
                                   Foreground="#CCCCCC" HorizontalAlignment="Center" Margin="0,5,0,0"/>
                    </StackPanel>
                    
                    <!-- Output Options -->
                    <GroupBox Header="Output" Margin="0,0,10,10">
                        <StackPanel>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <TextBox x:Name="TxtOutputFolder" Grid.Column="0" IsReadOnly="True"/>
                                <Button x:Name="BtnBrowseOutput" Content="..." Grid.Column="1" 
                                        Width="35" Margin="5,0,0,0" Style="{StaticResource SecondaryButton}"/>
                            </Grid>
                            <CheckBox x:Name="ChkAutoSave" Content="Auto-save generated images" 
                                      Foreground="#CCCCCC" Margin="0,10,0,0" IsChecked="True"/>
                            <CheckBox x:Name="ChkOpenFolder" Content="Open folder after saving" 
                                      Foreground="#CCCCCC" Margin="0,5,0,0"/>
                        </StackPanel>
                    </GroupBox>
                    
                    <!-- History -->
                    <GroupBox Header="Recent Prompts" Margin="0,0,10,10">
                        <ListBox x:Name="LstHistory" Height="100" Background="#2D2D30" 
                                 Foreground="#CCCCCC" BorderBrush="#3F3F46"/>
                    </GroupBox>
                </StackPanel>
            </ScrollViewer>
            
            <!-- Right Panel - Image Preview -->
            <GroupBox Grid.Column="1" Header="Generated Image">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    
                    <!-- Image Display -->
                    <Border Grid.Row="0" Background="#2D2D30" CornerRadius="4">
                        <Grid>
                            <TextBlock x:Name="TxtPlaceholder" 
                                       Text="Your generated image will appear here" 
                                       Foreground="#808080" 
                                       HorizontalAlignment="Center" 
                                       VerticalAlignment="Center"
                                       FontSize="16"/>
                            <Image x:Name="ImgPreview" 
                                   Stretch="Uniform" 
                                   Margin="10"
                                   RenderOptions.BitmapScalingMode="HighQuality"/>
                        </Grid>
                    </Border>
                    
                    <!-- Image Actions -->
                    <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,10,0,0">
                        <Button x:Name="BtnSaveAs" Content="Save As..." 
                                Style="{StaticResource SecondaryButton}" Margin="5,0" IsEnabled="False"/>
                        <Button x:Name="BtnCopyClipboard" Content="Copy to Clipboard" 
                                Style="{StaticResource SecondaryButton}" Margin="5,0" IsEnabled="False"/>
                        <Button x:Name="BtnOpenInViewer" Content="Open in Viewer" 
                                Style="{StaticResource SecondaryButton}" Margin="5,0" IsEnabled="False"/>
                        <Button x:Name="BtnVariation" Content="Create Variation" 
                                Style="{StaticResource SecondaryButton}" Margin="5,0" IsEnabled="False"/>
                    </StackPanel>
                </Grid>
            </GroupBox>
        </Grid>
        
        <!-- Status Bar -->
        <Border Grid.Row="2" Background="#007ACC" Height="28">
            <Grid Margin="10,0">
                <TextBlock x:Name="TxtStatus" Text="Ready - Enter a prompt and click Generate" 
                           Foreground="White" VerticalAlignment="Center"/>
                <TextBlock x:Name="TxtCost" Text="" 
                           Foreground="White" VerticalAlignment="Center" HorizontalAlignment="Right"/>
            </Grid>
        </Border>
    </Grid>
</Window>
"@

# ============================================
# Settings Dialog XAML
# ============================================
[xml]$settingsXaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="PlatypusAI Settings" 
        Height="450" Width="500"
        MinHeight="350" MinWidth="400"
        WindowStartupLocation="CenterOwner"
        Background="#1E1E1E"
        ResizeMode="CanResizeWithGrip">
    
    <Window.Resources>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#2D2D30"/>
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="Padding" Value="8,6"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>
        <Style TargetType="PasswordBox">
            <Setter Property="Background" Value="#2D2D30"/>
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="Padding" Value="8,6"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>
        <Style TargetType="Label">
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="FontSize" Value="13"/>
        </Style>
        <Style TargetType="GroupBox">
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
        </Style>
        <Style x:Key="ModernButton" TargetType="Button">
            <Setter Property="Background" Value="#007ACC"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="20,10"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <ScrollViewer Grid.Row="0" VerticalScrollBarVisibility="Auto">
        <StackPanel>
            <GroupBox Header="API Keys" Margin="0,0,0,15" Padding="10">
                <StackPanel>
                    <Label Content="OpenAI API Key:"/>
                    <PasswordBox x:Name="TxtOpenAIKey" Margin="0,5,0,10"/>
                    <TextBlock Text="Get your key at platform.openai.com" Foreground="#808080" FontSize="11"/>
                    
                    <Label Content="Stability AI API Key:" Margin="0,15,0,0"/>
                    <PasswordBox x:Name="TxtStabilityKey" Margin="0,5,0,10"/>
                    <TextBlock Text="Get your key at platform.stability.ai" Foreground="#808080" FontSize="11"/>
                </StackPanel>
            </GroupBox>
            
            <GroupBox Header="Microsoft Copilot / Azure OpenAI" Margin="0,0,0,15" Padding="10">
                <StackPanel>
                    <Label Content="Azure OpenAI Endpoint:"/>
                    <TextBox x:Name="TxtAzureEndpoint" Margin="0,5,0,5"/>
                    <TextBlock Text="e.g. https://your-resource.openai.azure.com" Foreground="#808080" FontSize="11"/>
                    
                    <Label Content="Azure OpenAI API Key:" Margin="0,10,0,0"/>
                    <PasswordBox x:Name="TxtAzureKey" Margin="0,5,0,5"/>
                    
                    <Label Content="Deployment Name:" Margin="0,10,0,0"/>
                    <TextBox x:Name="TxtAzureDeployment" Margin="0,5,0,5" Text="dall-e-3"/>
                    <TextBlock Text="Your DALL-E 3 deployment name in Azure" Foreground="#808080" FontSize="11"/>
                </StackPanel>
            </GroupBox>
            
            <GroupBox Header="Default Paths" Padding="10">
                <StackPanel>
                    <Label Content="Output Folder:"/>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBox x:Name="TxtSettingsOutputPath" Grid.Column="0"/>
                        <Button x:Name="BtnSettingsBrowse" Content="..." Grid.Column="1" 
                                Width="35" Margin="5,0,0,0" Style="{StaticResource ModernButton}"/>
                    </Grid>
                </StackPanel>
            </GroupBox>
        </StackPanel>
        </ScrollViewer>
        
        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,15,0,0">
            <Button x:Name="BtnSettingsSave" Content="Save" Style="{StaticResource ModernButton}" Width="80" Margin="0,0,10,0"/>
            <Button x:Name="BtnSettingsCancel" Content="Cancel" Style="{StaticResource ModernButton}" Width="80" Background="#3E3E42"/>
        </StackPanel>
    </Grid>
</Window>
"@

# ============================================
# Create Main Window
# ============================================
$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)

# Get all controls
$TxtPrompt = $window.FindName("TxtPrompt")
$CmbProvider = $window.FindName("CmbProvider")
$TxtProviderInfo = $window.FindName("TxtProviderInfo")
$CmbSize = $window.FindName("CmbSize")
$CmbQuality = $window.FindName("CmbQuality")
$CmbStyle = $window.FindName("CmbStyle")
$CmbCount = $window.FindName("CmbCount")
$BtnGenerate = $window.FindName("BtnGenerate")
$PnlProgress = $window.FindName("PnlProgress")
$TxtProgress = $window.FindName("TxtProgress")
$TxtOutputFolder = $window.FindName("TxtOutputFolder")
$BtnBrowseOutput = $window.FindName("BtnBrowseOutput")
$ChkAutoSave = $window.FindName("ChkAutoSave")
$ChkOpenFolder = $window.FindName("ChkOpenFolder")
$LstHistory = $window.FindName("LstHistory")
$ImgPreview = $window.FindName("ImgPreview")
$TxtPlaceholder = $window.FindName("TxtPlaceholder")
$BtnSaveAs = $window.FindName("BtnSaveAs")
$BtnCopyClipboard = $window.FindName("BtnCopyClipboard")
$BtnOpenInViewer = $window.FindName("BtnOpenInViewer")
$BtnVariation = $window.FindName("BtnVariation")
$BtnSettings = $window.FindName("BtnSettings")
$BtnHelp = $window.FindName("BtnHelp")
$TxtStatus = $window.FindName("TxtStatus")
$TxtCost = $window.FindName("TxtCost")

# Initialize controls
$TxtOutputFolder.Text = $script:Config.OutputFolder

# Track current image
$script:CurrentImagePath = $null
$script:PromptHistory = @()

# ============================================
# Helper Functions
# ============================================
function Update-Status {
    param([string]$Message)
    $TxtStatus.Text = $Message
    Write-AILog $Message
}

function Show-Progress {
    param([bool]$Show, [string]$Message = "Generating...")
    $PnlProgress.Visibility = if ($Show) { "Visible" } else { "Collapsed" }
    $TxtProgress.Text = $Message
    $BtnGenerate.IsEnabled = -not $Show
}

function Add-ToHistory {
    param([string]$Prompt)
    if ($Prompt -and $script:PromptHistory -notcontains $Prompt) {
        $script:PromptHistory = @($Prompt) + $script:PromptHistory | Select-Object -First 20
        $LstHistory.Items.Clear()
        foreach ($p in $script:PromptHistory) {
            $truncated = if ($p.Length -gt 50) { $p.Substring(0, 47) + "..." } else { $p }
            $LstHistory.Items.Add($truncated) | Out-Null
        }
    }
}

function Enable-ImageButtons {
    param([bool]$Enable)
    $BtnSaveAs.IsEnabled = $Enable
    $BtnCopyClipboard.IsEnabled = $Enable
    $BtnOpenInViewer.IsEnabled = $Enable
    $BtnVariation.IsEnabled = $Enable
}

function Get-UniqueFilename {
    param([string]$Folder, [string]$BaseName, [string]$Extension)
    $counter = 1
    $filename = "$BaseName$Extension"
    while (Test-Path (Join-Path $Folder $filename)) {
        $filename = "${BaseName}_$counter$Extension"
        $counter++
    }
    return Join-Path $Folder $filename
}

# ============================================
# Event Handlers
# ============================================

# Provider change
$CmbProvider.Add_SelectionChanged({
    $provider = $CmbProvider.SelectedItem.Content
    if ($provider -match "OpenAI DALL-E") {
        $TxtProviderInfo.Text = "Using DALL-E 3 - Best quality, `$0.04-0.12 per image"
        $CmbQuality.IsEnabled = $true
        $CmbStyle.IsEnabled = $true
        $CmbCount.SelectedIndex = 0
        $CmbCount.IsEnabled = $false
    } elseif ($provider -match "Copilot|Azure") {
        $TxtProviderInfo.Text = "Using Azure OpenAI (DALL-E 3) - Requires Azure subscription"
        $CmbQuality.IsEnabled = $true
        $CmbStyle.IsEnabled = $true
        $CmbCount.SelectedIndex = 0
        $CmbCount.IsEnabled = $false
    } else {
        $TxtProviderInfo.Text = "Using Stable Diffusion XL - Fast generation, ~`$0.002 per image"
        $CmbQuality.IsEnabled = $false
        $CmbStyle.IsEnabled = $false
        $CmbCount.IsEnabled = $true
    }
})

# Browse output folder
$BtnBrowseOutput.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select output folder for generated images"
    $dialog.SelectedPath = $TxtOutputFolder.Text
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $TxtOutputFolder.Text = $dialog.SelectedPath
        $script:Config.OutputFolder = $dialog.SelectedPath
    }
})

# History selection
$LstHistory.Add_SelectionChanged({
    if ($LstHistory.SelectedIndex -ge 0 -and $LstHistory.SelectedIndex -lt $script:PromptHistory.Count) {
        $TxtPrompt.Text = $script:PromptHistory[$LstHistory.SelectedIndex]
    }
})

# Generate button
$BtnGenerate.Add_Click({
    $prompt = $TxtPrompt.Text.Trim()
    if (-not $prompt) {
        [System.Windows.MessageBox]::Show("Please enter a prompt.", "PlatypusAI", "OK", "Warning")
        return
    }
    
    $provider = $CmbProvider.SelectedItem.Content
    $size = $CmbSize.SelectedItem.Content
    $quality = $CmbQuality.SelectedItem.Content
    $style = $CmbStyle.SelectedItem.Content
    $count = [int]($CmbCount.SelectedItem.Content)
    
    Show-Progress -Show $true -Message "Connecting to AI service..."
    $TxtPlaceholder.Visibility = "Collapsed"
    
    # Run generation in background
    $runspace = [runspacefactory]::CreateRunspace()
    $runspace.ApartmentState = "STA"
    $runspace.ThreadOptions = "ReuseThread"
    $runspace.Open()
    
    $runspace.SessionStateProxy.SetVariable("provider", $provider)
    $runspace.SessionStateProxy.SetVariable("prompt", $prompt)
    $runspace.SessionStateProxy.SetVariable("size", $size)
    $runspace.SessionStateProxy.SetVariable("quality", $quality)
    $runspace.SessionStateProxy.SetVariable("style", $style)
    $runspace.SessionStateProxy.SetVariable("count", $count)
    $runspace.SessionStateProxy.SetVariable("config", $script:Config)
    $runspace.SessionStateProxy.SetVariable("outputFolder", $TxtOutputFolder.Text)
    
    $powershell = [powershell]::Create()
    $powershell.Runspace = $runspace
    
    [void]$powershell.AddScript({
        param($provider, $prompt, $size, $quality, $style, $count, $config, $outputFolder)
        
        try {
            if ($provider -match "OpenAI DALL-E") {
                $apiKey = $config.OpenAI_ApiKey
                if (-not $apiKey) { throw "OpenAI API key not set" }
                
                $headers = @{
                    "Authorization" = "Bearer $apiKey"
                    "Content-Type" = "application/json"
                }
                
                $body = @{
                    model = "dall-e-3"
                    prompt = $prompt
                    n = 1
                    size = $size
                    quality = $quality
                    style = $style
                    response_format = "url"
                } | ConvertTo-Json
                
                $response = Invoke-RestMethod -Uri "https://api.openai.com/v1/images/generations" `
                    -Method Post -Headers $headers -Body $body -TimeoutSec 120
                
                $imageUrl = $response.data[0].url
                $revisedPrompt = $response.data[0].revised_prompt
                
                # Download image
                $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
                $filename = "platypusai_$timestamp.png"
                $filepath = Join-Path $outputFolder $filename
                
                $webClient = New-Object System.Net.WebClient
                $webClient.DownloadFile($imageUrl, $filepath)
                
                return @{
                    Success = $true
                    ImagePath = $filepath
                    RevisedPrompt = $revisedPrompt
                    Provider = "OpenAI"
                }
            }
            elseif ($provider -match "Copilot|Azure") {
                $apiKey = $config.AzureOpenAI_ApiKey
                $endpoint = $config.AzureOpenAI_Endpoint
                $deploymentName = $config.AzureOpenAI_DeploymentName
                
                if (-not $apiKey) { throw "Azure OpenAI API key not set. Configure in Settings." }
                if (-not $endpoint) { throw "Azure OpenAI endpoint not set. Configure in Settings." }
                if (-not $deploymentName) { $deploymentName = "dall-e-3" }
                
                # Remove trailing slash from endpoint
                $endpoint = $endpoint.TrimEnd('/')
                
                $headers = @{
                    "api-key" = $apiKey
                    "Content-Type" = "application/json"
                }
                
                $body = @{
                    prompt = $prompt
                    n = 1
                    size = $size
                    quality = $quality
                    style = $style
                } | ConvertTo-Json
                
                $uri = "$endpoint/openai/deployments/$deploymentName/images/generations?api-version=2024-02-01"
                
                $response = Invoke-RestMethod -Uri $uri `
                    -Method Post -Headers $headers -Body $body -TimeoutSec 120
                
                $imageUrl = $response.data[0].url
                $revisedPrompt = $response.data[0].revised_prompt
                
                # Download image
                $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
                $filename = "platypusai_$timestamp.png"
                $filepath = Join-Path $outputFolder $filename
                
                $webClient = New-Object System.Net.WebClient
                $webClient.DownloadFile($imageUrl, $filepath)
                
                return @{
                    Success = $true
                    ImagePath = $filepath
                    RevisedPrompt = $revisedPrompt
                    Provider = "Azure"
                }
            }
            else {
                $apiKey = $config.StabilityAI_ApiKey
                if (-not $apiKey) { throw "Stability AI API key not set" }
                
                $dims = $size -split 'x'
                $width = [int]$dims[0]
                $height = [int]$dims[1]
                
                $headers = @{
                    "Authorization" = "Bearer $apiKey"
                    "Content-Type" = "application/json"
                    "Accept" = "application/json"
                }
                
                $body = @{
                    text_prompts = @(@{ text = $prompt; weight = 1 })
                    cfg_scale = 7
                    height = $height
                    width = $width
                    steps = 30
                    samples = $count
                } | ConvertTo-Json -Depth 5
                
                $response = Invoke-RestMethod -Uri "https://api.stability.ai/v1/generation/stable-diffusion-xl-1024-v1-0/text-to-image" `
                    -Method Post -Headers $headers -Body $body -TimeoutSec 120
                
                $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
                $filename = "platypusai_$timestamp.png"
                $filepath = Join-Path $outputFolder $filename
                
                $bytes = [Convert]::FromBase64String($response.artifacts[0].base64)
                [System.IO.File]::WriteAllBytes($filepath, $bytes)
                
                return @{
                    Success = $true
                    ImagePath = $filepath
                    RevisedPrompt = $null
                    Provider = "Stability"
                }
            }
        }
        catch {
            return @{
                Success = $false
                Error = $_.Exception.Message
            }
        }
    })
    
    [void]$powershell.AddArgument($provider)
    [void]$powershell.AddArgument($prompt)
    [void]$powershell.AddArgument($size)
    [void]$powershell.AddArgument($quality)
    [void]$powershell.AddArgument($style)
    [void]$powershell.AddArgument($count)
    [void]$powershell.AddArgument($script:Config)
    [void]$powershell.AddArgument($TxtOutputFolder.Text)
    
    $asyncResult = $powershell.BeginInvoke()
    
    # Timer to check completion
    $timer = New-Object System.Windows.Threading.DispatcherTimer
    $timer.Interval = [TimeSpan]::FromMilliseconds(100)
    $timer.Tag = @{ PS = $powershell; Result = $asyncResult; Prompt = $prompt }
    $timer.Add_Tick({
        $t = $this
        $data = $t.Tag
        
        if ($data.Result.IsCompleted) {
            $t.Stop()
            
            try {
                $result = $data.PS.EndInvoke($data.Result)
                
                if ($result.Success) {
                    # Load and display image
                    $bitmap = New-Object System.Windows.Media.Imaging.BitmapImage
                    $bitmap.BeginInit()
                    $bitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
                    $bitmap.UriSource = New-Object System.Uri($result.ImagePath)
                    $bitmap.EndInit()
                    $bitmap.Freeze()
                    
                    $ImgPreview.Source = $bitmap
                    $script:CurrentImagePath = $result.ImagePath
                    
                    Enable-ImageButtons -Enable $true
                    Add-ToHistory -Prompt $data.Prompt
                    
                    $msg = "Image generated successfully! Saved to: $($result.ImagePath)"
                    if ($result.RevisedPrompt) {
                        $msg += " (DALL-E revised your prompt)"
                    }
                    Update-Status $msg
                    
                    # Calculate cost estimate
                    $cost = if ($result.Provider -eq "OpenAI") { "`$0.04-0.12" } elseif ($result.Provider -eq "Azure") { "Azure pricing" } else { "~`$0.002" }
                    $TxtCost.Text = "Est. cost: $cost"
                    
                    if ($ChkOpenFolder.IsChecked) {
                        Start-Process explorer.exe -ArgumentList "/select,`"$($result.ImagePath)`""
                    }
                }
                else {
                    Update-Status "Error: $($result.Error)"
                    [System.Windows.MessageBox]::Show("Failed to generate image:`n`n$($result.Error)", "PlatypusAI Error", "OK", "Error")
                }
            }
            catch {
                Update-Status "Error: $_"
            }
            finally {
                Show-Progress -Show $false
                $data.PS.Dispose()
            }
        }
    })
    $timer.Start()
    
    $TxtProgress.Text = "Generating image with AI..."
})

# Save As button
$BtnSaveAs.Add_Click({
    if ($script:CurrentImagePath -and (Test-Path $script:CurrentImagePath)) {
        $dialog = New-Object System.Windows.Forms.SaveFileDialog
        $dialog.Filter = "PNG Image|*.png|JPEG Image|*.jpg|All Files|*.*"
        $dialog.FileName = [System.IO.Path]::GetFileName($script:CurrentImagePath)
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            Copy-Item $script:CurrentImagePath $dialog.FileName -Force
            Update-Status "Image saved to: $($dialog.FileName)"
        }
    }
})

# Copy to Clipboard
$BtnCopyClipboard.Add_Click({
    if ($script:CurrentImagePath -and (Test-Path $script:CurrentImagePath)) {
        try {
            $image = [System.Drawing.Image]::FromFile($script:CurrentImagePath)
            [System.Windows.Forms.Clipboard]::SetImage($image)
            $image.Dispose()
            Update-Status "Image copied to clipboard!"
        } catch {
            Update-Status "Failed to copy to clipboard: $_"
        }
    }
})

# Open in Viewer
$BtnOpenInViewer.Add_Click({
    if ($script:CurrentImagePath -and (Test-Path $script:CurrentImagePath)) {
        Start-Process $script:CurrentImagePath
    }
})

# Variation button (for future enhancement)
$BtnVariation.Add_Click({
    [System.Windows.MessageBox]::Show("Image variations will be available in a future update.", "Coming Soon", "OK", "Information")
})

# Settings button
$BtnSettings.Add_Click({
    $settingsReader = New-Object System.Xml.XmlNodeReader $settingsXaml
    $settingsWindow = [Windows.Markup.XamlReader]::Load($settingsReader)
    $settingsWindow.Owner = $window
    
    $TxtOpenAIKey = $settingsWindow.FindName("TxtOpenAIKey")
    $TxtStabilityKey = $settingsWindow.FindName("TxtStabilityKey")
    $TxtAzureEndpoint = $settingsWindow.FindName("TxtAzureEndpoint")
    $TxtAzureKey = $settingsWindow.FindName("TxtAzureKey")
    $TxtAzureDeployment = $settingsWindow.FindName("TxtAzureDeployment")
    $TxtSettingsOutputPath = $settingsWindow.FindName("TxtSettingsOutputPath")
    $BtnSettingsBrowse = $settingsWindow.FindName("BtnSettingsBrowse")
    $BtnSettingsSave = $settingsWindow.FindName("BtnSettingsSave")
    $BtnSettingsCancel = $settingsWindow.FindName("BtnSettingsCancel")
    
    # Load current values
    $TxtOpenAIKey.Password = $script:Config.OpenAI_ApiKey
    $TxtStabilityKey.Password = $script:Config.StabilityAI_ApiKey
    $TxtAzureEndpoint.Text = $script:Config.AzureOpenAI_Endpoint
    $TxtAzureKey.Password = $script:Config.AzureOpenAI_ApiKey
    $TxtAzureDeployment.Text = $script:Config.AzureOpenAI_DeploymentName
    $TxtSettingsOutputPath.Text = $script:Config.OutputFolder
    
    $BtnSettingsBrowse.Add_Click({
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.SelectedPath = $TxtSettingsOutputPath.Text
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $TxtSettingsOutputPath.Text = $dialog.SelectedPath
        }
    })
    
    $BtnSettingsSave.Add_Click({
        $script:Config.OpenAI_ApiKey = $TxtOpenAIKey.Password
        $script:Config.StabilityAI_ApiKey = $TxtStabilityKey.Password
        $script:Config.AzureOpenAI_Endpoint = $TxtAzureEndpoint.Text
        $script:Config.AzureOpenAI_ApiKey = $TxtAzureKey.Password
        $script:Config.AzureOpenAI_DeploymentName = $TxtAzureDeployment.Text
        $script:Config.OutputFolder = $TxtSettingsOutputPath.Text
        $TxtOutputFolder.Text = $TxtSettingsOutputPath.Text
        Save-Config
        $settingsWindow.Close()
        Update-Status "Settings saved!"
    })
    
    $BtnSettingsCancel.Add_Click({
        $settingsWindow.Close()
    })
    
    $settingsWindow.ShowDialog()
})

# Help button
$BtnHelp.Add_Click({
    $helpText = @"
PlatypusAI - AI Image Generator

GETTING STARTED:
1. Click Settings and enter your API key(s)
2. Type a description of the image you want
3. Select options (size, quality, style)
4. Click Generate Image

TIPS FOR BETTER RESULTS:
- Be specific and detailed in your prompts
- Include style keywords: "digital art", "photograph", "oil painting"
- Describe lighting, mood, and composition
- Mention specific artists or art styles for inspiration

API KEYS:
- OpenAI: Get from platform.openai.com/api-keys
- Stability AI: Get from platform.stability.ai
- Microsoft Copilot (Azure): Use your Azure OpenAI endpoint and key

PRICING (approximate):
- DALL-E 3 Standard: $0.04 per image
- DALL-E 3 HD: $0.08-0.12 per image
- Stability AI: ~$0.002 per image
- Azure OpenAI: See your Azure pricing tier
"@
    [System.Windows.MessageBox]::Show($helpText, "PlatypusAI Help", "OK", "Information")
})

# ============================================
# Show Window
# ============================================
Update-Status "Ready - Enter a prompt and click Generate"
$window.ShowDialog() | Out-Null
