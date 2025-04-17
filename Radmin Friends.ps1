# Ensure BurntToast module is installed
if (-not (Get-Module -ListAvailable -Name BurntToast)) {
    Install-Module -Name BurntToast -Force -Scope CurrentUser
}

$logPath = "C:\ProgramData\Famatech\Radmin VPN\service.log"
$friendsLogPath = "$PSScriptRoot\Friends.log"
$friendsFile = "$PSScriptRoot\Friends.txt"
$lastLine = ""
$userStatus = @{}  # Tracks the online/offline status of users
$webhookUrl = "https://discord.com/api/webhooks/1357101621846343861/bYHnEBttg2EZSX0a3T4h4PTptcC0J0HjGArOAyAD1Qu-ink-fMyzgI6jBX_-dxvY7s4U" # Discord webhook URL
$discordChannelId = "1298615427382906932"  # Discord channel ID
$discordBotToken = "MTM1NzM5MTU3NTI1MTk0MzQ4Nw.GvBgam.SrjFFnHe7hPaXiSiJyT5uAzEP5Cf2V-o5xaRaQ"  # Discord bot token
$myUsername = "Koriebonx98"  # Username
$lastReadMessageIdFile = "$PSScriptRoot\LastReadMessageId.txt"
$processedMessageIds = @()  # Store IDs of processed messages
$global:toastedMessages = @{}  # Store messages that have been toasted

# Clear Friends.log at the start of the script
Clear-Content -Path $friendsLogPath -ErrorAction SilentlyContinue

# Function to load friends list from Friends.txt
function Load-Friends {
    if (-not (Test-Path -Path $friendsFile)) {
        Write-Host "Friends.txt not found. Please create it next to the script with the format: RadminID,Username,IPAddress,IconFolderName,IconID"
        Exit
    }
    $friends = @{}
    Get-Content -Path $friendsFile | ForEach-Object {
        $fields = $_ -split ","
        if ($fields.Length -eq 5) {
            $radminID = $fields[0].Trim()
            $username = $fields[1].Trim()
            $ipAddress = $fields[2].Trim()
            $iconFolder = $fields[3].Trim()
            $iconID = $fields[4].Trim()
            $friends[$radminID] = @{
                "Username" = $username
                "IPAddress" = $ipAddress
                "IconFolder" = $iconFolder
                "IconID" = $iconID
            }
            Write-Host "Loaded friend: ID=$radminID, Username=$username, IPAddress=$ipAddress, IconFolder=$iconFolder, IconID=$iconID"
        } else {
            Write-Host "Invalid entry in Friends.txt: $($_)"
        }
    }
    return $friends
}

# Load friends into a hashtable
$friendsList = Load-Friends

# Function to log user status to Friends.log
function Log-UserStatus {
    param (
        [string]$username,
        [string]$status,
        [string]$timestamp,
        [string]$source
    )
    $logEntry = "[$timestamp] $username - $status (Source: $source)"
    Add-Content -Path $friendsLogPath -Value $logEntry
}

# Function to log errors and messages
function Log-Error {
    param (
        [string]$errorMessage
    )
    $timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    Add-Content -Path $friendsLogPath -Value "[$timestamp] ERROR: $errorMessage"
}

# Function to log messages read from Discord
function Log-DiscordMessage {
    param (
        [string]$message
    )
    $timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    Add-Content -Path $friendsLogPath -Value "[$timestamp] Discord Message: $message (Source: Discord)"
}

# Function to play a sound
function Play-Sound {
    param (
        [string]$soundPath
    )
    if (Test-Path -Path $soundPath) {
        $player = New-Object Media.SoundPlayer $soundPath
        $player.PlaySync()
    } else {
        Write-Host "Sound file not found at $soundPath"
    }
}

# Function to show Windows notification using BurntToast and play a sound
function Show-Notification {
    param (
        [string]$title,
        [string]$message,
        [string]$iconFolder,
        [string]$iconID
    )
    $notificationKey = "$title - $message"
    if (-not $global:toastedMessages.ContainsKey($notificationKey)) {
        $iconPath = "$PSScriptRoot\Icons\$iconFolder\$iconID.png"
        if (Test-Path -Path $iconPath) {
            Write-Host "Icon found at $iconPath"
        } else {
            Write-Host "Icon not found at $iconPath, using default icon"
            $iconPath = "C:\Windows\System32\shell32.dll"
        }
        New-BurntToastNotification -Text $title, $message -AppLogo $iconPath

        # Play a sound (add the path to your sound file here)
        $soundPath = "C:\Windows\Media\Windows Notify System Generic.wav"
        Play-Sound -soundPath $soundPath

        # Add the notification key to the list of toasted messages
        $global:toastedMessages[$notificationKey] = $true
    }
}

# Function to send a message to Discord webhook and log it
function Send-DiscordMessage {
    param (
        [string]$message
    )
    $payload = @{
        content = $message
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri $webhookUrl -Method Post -ContentType "application/json" -Body $payload
        # Log the sent message
        $timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        Add-Content -Path $friendsLogPath -Value "[$timestamp] Sent to Discord: $message (Source: Local)"
    } catch {
        $timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        $errorMessage = "Failed to send message to Discord: $_"
        Add-Content -Path $friendsLogPath -Value "[$timestamp] ERROR: $errorMessage"
        Log-Error -errorMessage $errorMessage
    }
}

# Function to check log for updates
function Check-Log {
    $newLine = Get-Content -Path $logPath -Tail 1

    if ($newLine -ne $lastLine) {
        $global:lastLine = $newLine
        if ($newLine -match "Connected to (?<RadminID>\d+)/'(?<User>.*?)' via TCP/(outgoing|incoming)") {
            $radminID = $Matches['RadminID']
            $username = $Matches['User']
            $timestamp = $newLine.Substring(0, 23)

            # Notify when user comes online
            if ($friendsList.ContainsKey($radminID)) {
                $userStatus[$radminID] = "Online"  # Update status
                Show-Notification -title $username -message "Online" -iconFolder $friendsList[$radminID]["IconFolder"] -iconID $friendsList[$radminID]["IconID"]
                Log-UserStatus -username $username -status "Online" -timestamp $timestamp -source "Local"
            }
        } elseif ($newLine -match "Node (?<RadminID>\d+)/'(?<User>.*?)' disconnected") {
            $radminID = $Matches['RadminID']
            $username = $Matches['User']
            $timestamp = $newLine.Substring(0, 23)

            # Notify when user goes offline
            if ($friendsList.ContainsKey($radminID) -and $userStatus[$radminID] -eq "Online") {
                $userStatus[$radminID] = "Offline"  # Update status
                Show-Notification -title $username -message "Offline" -iconFolder $friendsList[$radminID]["IconFolder"] -iconID $friendsList[$radminID]["IconID"]
                Log-UserStatus -username $username -status "Offline" -timestamp $timestamp -source "Local"
            }
        }
    }
}

# Function to get the latest user RID from services.log
function Get-User-RID-From-ServicesLog {
    param (
        [string]$logFilePath
    )

    # Initialize variables
    $latestRID = $null
    $latestTimestamp = [datetime]::MinValue

    # Read the log file content
    $logContent = Get-Content -Path $logFilePath -ErrorAction SilentlyContinue

    foreach ($logEntry in $logContent) {
        if ($logEntry -match "^(?<timestamp>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+INFO\s+RID:\s+(?<rid>\d+)$") {
            $timestamp = [datetime]::ParseExact($Matches['timestamp'], "yyyy.MM.dd HH:mm:ss.fff", $null)
            $rid = $Matches['rid']

            if ($timestamp -gt $latestTimestamp) {
                $latestTimestamp = $timestamp
                $latestRID = $rid
            }
        }
    }

    return $latestRID
}

# Path to the services.log file
$servicesLogPath = "C:\ProgramData\Famatech\Radmin VPN\service.log"
$myRadminID = Get-User-RID-From-ServicesLog -logFilePath $servicesLogPath
Write-Host "Latest RID: $myRadminID"

function Check-For-New-Messages-To-Toast {
    $loggedMessages = Get-Content -Path $friendsLogPath -ErrorAction SilentlyContinue
    foreach ($logEntry in $loggedMessages) {
        Write-Host "Processing log entry: $logEntry"
        Write-Host "Content: $logEntry"

        if ($logEntry -match "\[(?<timestamp>[^\]]+)\] Discord Message: Game Status: \((?<id>\d+)\), \((?<username>.*?)\): (?<content>.*?) \(Source: Discord\)") {
            $timestamp = $Matches['timestamp']
            $username = $Matches['username']
            $id = $Matches['id']
            $content = $Matches['content']

            # Debug output for content
            Write-Host "Content: $content"

            # Check if the log entry contains "Started: \"[GameName]\""
            if ($content -match 'Started\s*:\s*"([^"]+)"') {
                $gameName = $Matches[1]

                if ($friendsList.ContainsKey($id)) {
                    $iconFolder = $friendsList[$id]["IconFolder"]
                    $iconID = $friendsList[$id]["IconID"]
                } else {
                    Write-Host "ID $id not found in friends list"
                    $iconFolder = "default"  # Default icon folder if not found in friends list
                    $iconID = "default"  # Default icon ID if not found in friends list
                }

                # Debug output
                Write-Host "Log Entry: $logEntry"
                Write-Host "Parsed Username: $username"
                Write-Host "Parsed Game Name: $gameName"
                Write-Host "IconFolder: $iconFolder"
                Write-Host "IconID: $iconID"

                $notificationKey = "$timestamp - $username - Started: $gameName"
                if (-not $global:toastedMessages.ContainsKey($notificationKey)) {
                    Write-Host "Sending Toast Notification for $username starting $gameName"
                    Show-Notification -title $username -message "Started: $gameName" -iconFolder $iconFolder -iconID $iconID
                    $global:toastedMessages[$notificationKey] = $true
                } else {
                    Write-Host "Notification already sent for $username starting $gameName"
                }
            } else {
                Write-Host "No game start detected in log entry: $logEntry"
            }
        } else {
            Write-Host "No matching log entry found: $logEntry"
        }
    }
}

# Create Friends.log if it doesn't exist
if (-not (Test-Path -Path $friendsLogPath)) {
    New-Item -Path $friendsLogPath -ItemType File | Out-Null
    Write-Host "Created Friends.log at $friendsLogPath"
}

# Initialize the last line from the log
if (Test-Path -Path $logPath) {
    $lastLine = Get-Content -Path $logPath -Tail 1
}

# Send initial message to Discord indicating the user is online
$message = "($myRadminID), ($myUsername): Online"
Send-DiscordMessage -message $message

# Log the initial message to Friends.log
Log-UserStatus -username $myUsername -status "Online" -timestamp (Get-Date -Format "yyyy-MM-dd HH:mm:ss") -source "Local"

# Variables for discord message reading
$BotToken = "MTM1NzM5MTU3NTI1MTk0MzQ4Nw.GvBgam.SrjFFnHe7hPaXiSiJyT5uAzEP5Cf2V-o5xaRaQ"  # Your bot token
$ChannelId = "1357101571250454758"  # Your channel ID
$BaseUrl = "https://discord.com/api/v9"
$LogFile = "$PSScriptRoot\Friends.log"

function Get-LastReadMessageId {
    if (Test-Path -Path $lastReadMessageIdFile) {
        return Get-Content -Path $lastReadMessageIdFile
    } else {
        return ""
    }
}

function Set-LastReadMessageId {
    param (
        [string]$messageId
    )
    Set-Content -Path $lastReadMessageIdFile -Value $messageId
}

$lastReadMessageId = Get-LastReadMessageId

# Check if the log file exists, create it if not
if (-not (Test-Path $LogFile)) {
    Write-Host "Log file not found. Creating $LogFile..."
    New-Item -Path $LogFile -ItemType File -Force | Out-Null
}

# Construct headers
$Headers = @{
    Authorization = "Bot $BotToken"
    "Content-Type" = "application/json"
    "User-Agent" = "DiscordBot (https://example.com, 1.0)"
}

# API URL for fetching messages
$Url = "$BaseUrl/channels/$ChannelId/messages?limit=50"

# Function to read messages from Discord channel, log if not already logged
function Read-DiscordMessages {
    try {
        Write-Host "Fetching messages from Discord channel ID $ChannelId..."
        $Response = Invoke-RestMethod -Uri $Url -Headers $Headers -Method Get -ErrorAction Stop

        if ($Response) {
            $existingLogEntries = Get-Content -Path $LogFile -ErrorAction SilentlyContinue

            $messagesToLog = @()
            $newLastReadMessageId = $lastReadMessageId
            foreach ($Message in $Response | Sort-Object id) {
                if ($Message.id -gt $lastReadMessageId -and -not $processedMessageIds.Contains($Message.id)) {
                    $LogEntry = "$($Message.author.username): $($Message.content)"
                    if (-not ($existingLogEntries -contains $LogEntry)) {
                        $messagesToLog += $LogEntry
                    }

                    # Debug: Output the message content to ensure it's being read correctly
                    Write-Host "Read message: $($Message.content)"
                    
                    # Mark the message as processed
                    $processedMessageIds += $Message.id
                }
                $newLastReadMessageId = $Message.id
            }

            # Update the last read message ID
            if ($Response.Count -gt 0) {
                Set-LastReadMessageId -messageId $newLastReadMessageId
                $lastReadMessageId = $newLastReadMessageId
            }

            # Sort messages by timestamp (eldest first, newest last) and log them
            $messagesToLog = $messagesToLog | Sort-Object
            foreach ($LogEntry in $messagesToLog) {
                Add-Content -Path $LogFile -Value $LogEntry
                Log-DiscordMessage -message $LogEntry
            }

            Write-Host "Messages successfully logged to $LogFile"
        } else {
            Write-Host "No messages were fetched. Check permissions or channel ID."
        }
    } catch {
        Write-Error "Failed to fetch messages: $_"
        if ($_.Exception.Response -ne $null) {
            Write-Host "HTTP Status Code: $($_.Exception.Response.StatusCode)"
            Write-Host "Response Content: $($_.Exception.Response.GetResponseStream() | % { Read-Host })"
        }
    }
}

# Continuously monitor log and Discord messages
Write-Host "Monitoring log for changes and reading Discord messages. Press Ctrl+C to stop."
while ($true) {
    Check-Log
    Read-DiscordMessages
    Check-For-New-Messages-To-Toast
    Start-Sleep -Seconds 20
}