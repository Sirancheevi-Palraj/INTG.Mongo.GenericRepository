param(
    # GitHub organization
    [string]$Org = "CitiInternal",

    # Topic filter (same as in your GitHub search URL)
    # https://github.com/search?q=topic%3Als-project-name-intg+org%3ACitiInternal&type=Repositories
    [string]$Topic = "",

    # Base folder where everything will be cloned
    [string]$TargetDir = "C:\INTG",

    # File name (in same folder as this script) containing repo URLs line by line
    [string]$RepoListFileName = "repos.txt"
)

Write-Host "==============================================="
Write-Host "   CitiInternal – Bulk Repo Cloner"
Write-Host "==============================================="
Write-Host "Org        : $Org"
Write-Host "Topic      : $Topic"
Write-Host "Target dir : $TargetDir"
Write-Host ""

# ---------------------------------------------------------
# Helpers
# ---------------------------------------------------------

function Ensure-Git {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Error "Git is not installed or not in PATH. Please install Git for Windows and try again."
        Read-Host "Press ENTER to exit..."
        exit 1
    }
}

function Ensure-TargetDir {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        Write-Host "Creating target directory: $Path"
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Clone-RepoList {
    param(
        [string[]]$RepoUrls,
        [string]$BaseDir
    )

    Ensure-Git
    Ensure-TargetDir -Path $BaseDir

    foreach ($url in $RepoUrls) {
        $trimmed = $url.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }

        # Derive repo folder name from URL
        # e.g. https://github.com/CitiInternal/my-repo.git -> my-repo
        $name = $trimmed.Split('/')[-1]
        if ($name -like "*.git") {
            $name = $name.Substring(0, $name.Length - 4)
        }

        $destPath = Join-Path $BaseDir $name

        Write-Host "----------------------------------------------------"
        Write-Host "Repo URL : $trimmed"
        Write-Host "Name     : $name"
        Write-Host "Folder   : $destPath"

        if (Test-Path $destPath) {
            Write-Host "[$name] already exists. Pulling latest changes..."
            try {
                git -C $destPath pull
            }
            catch {
                Write-Warning "Failed to pull latest for $name. Error: $_"
            }
        } else {
            Write-Host "Cloning [$name]..."
            try {
                # Uses Windows credential manager / Git Credential Manager
                # If not logged in, will trigger browser login once
                git clone $trimmed $destPath
            }
            catch {
                Write-Warning "Failed to clone $name. Error: $_"
            }
        }
    }
}

function Clone-FromGitHub {
    param(
        [string]$Org,
        [string]$Topic,
        [string]$TargetDir
    )

    Write-Host ""
    Write-Host "=== OPTION 1: From GitHub search (org + topic) ==="

    # Force TLS 1.2 for API calls (older PowerShell)
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    } catch { }

    # Get GitHub token (required to see private repos)
    $token = $env:GITHUB_TOKEN
    if (-not $token) {
        Write-Host "GITHUB_TOKEN environment variable not found."
        Write-Host "Because CitiInternal repos are private, a GitHub Personal Access Token is required"
        Write-Host "ONLY to list repositories via the GitHub API."
        Write-Host ""
        $secureToken = Read-Host "Paste your GitHub token (input is hidden, not stored)" -AsSecureString
        $token = [Runtime.InteropServices.Marshal]::PtrToStringUni(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
        )
    }

    $headers = @{
        "Accept"        = "application/vnd.github+json"
        "User-Agent"    = "INTG-Bulk-Clone-Script"
        "Authorization" = "Bearer $token"
    }

    $perPage = 100
    $page    = 1
    $hasMore = $true

    $allRepoUrls = @()

    while ($hasMore) {
        if ([string]::IsNullOrWhiteSpace($Topic)) {
            # If topic is empty, just list org repos
            $searchUrl = "https://api.github.com/search/repositories?q=org:$Org&per_page=$perPage&page=$page"
        } else {
            # Your default: filter by topic + org
            $searchUrl = "https://api.github.com/search/repositories?q=topic:$Topic+org:$Org&per_page=$perPage&page=$page"
        }

        Write-Host ""
        Write-Host "Calling GitHub search API:"
        Write-Host "  $searchUrl"

        try {
            $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
        }
        catch {
            Write-Error "Failed to call GitHub API. Check your token and CitiInternal org access."
            Write-Error "$_"
            Write-Host ""
            Write-Host ">>> Suggestion: Use OPTION 2 (From File) with a repos.txt file containing repo URLs."
            return
        }

        if (-not $response.items -or $response.items.Count -eq 0) {
            Write-Host "No repositories found for this page. Stopping."
            break
        }

        foreach ($repo in $response.items) {
            $cloneUrl = $repo.clone_url     # e.g. https://github.com/CitiInternal/repo.git
            $allRepoUrls += $cloneUrl
        }

        $totalCount = [int]$response.total_count
        $maxPages   = [math]::Ceiling($totalCount / $perPage)

        $page++
        if ($page -gt $maxPages) {
            $hasMore = $false
        }
    }

    if ($allRepoUrls.Count -eq 0) {
        Write-Host "No repositories collected from GitHub search."
        Write-Host ">>> Suggestion: Use OPTION 2 (From File) with a repos.txt file."
        return
    }

    Write-Host ""
    Write-Host "Total repositories to process from GitHub search: $($allRepoUrls.Count)"
    Clone-RepoList -RepoUrls $allRepoUrls -BaseDir $TargetDir
}

function Clone-FromFile {
    param(
        [string]$TargetDir,
        [string]$RepoListFileName
    )

    Write-Host ""
    Write-Host "=== OPTION 2: From File (repos.txt) ==="

    # Script directory (where this .ps1 sits)
    $scriptDir =
        if ($PSScriptRoot) { $PSScriptRoot }
        else { Split-Path -Parent $MyInvocation.MyCommand.Path }

    $filePath = Join-Path $scriptDir $RepoListFileName

    if (-not (Test-Path $filePath)) {
        Write-Warning "Repo list file not found: $filePath"
        Write-Host ""
        Write-Host "Create a text file called '$RepoListFileName' in the same folder as this script."
        Write-Host "Each line should be a Git repo URL, for example:"
        Write-Host "  https://github.com/CitiInternal/project-one.git"
        Write-Host "  https://github.com/CitiInternal/project-two.git"
        Write-Host ""
        return
    }

    $urls = Get-Content -Path $filePath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    if (-not $urls -or $urls.Count -eq 0) {
        Write-Warning "No URLs found in $filePath"
        return
    }

    Write-Host "Found $($urls.Count) repo URL(s) in $filePath"
    Clone-RepoList -RepoUrls $urls -BaseDir $TargetDir
}

# ---------------------------------------------------------
# Menu: choose how to clone
# ---------------------------------------------------------

Write-Host "Select source:"
Write-Host "  1. From GitHub (org + topic search)"
Write-Host "  2. From File (repos.txt in script folder)"
Write-Host ""

$choice = Read-Host "Enter choice (1 or 2)"

switch ($choice) {
    "1" {
        Clone-FromGitHub -Org $Org -Topic $Topic -TargetDir $TargetDir
    }
    "2" {
        Clone-FromFile -TargetDir $TargetDir -RepoListFileName $RepoListFileName
    }
    default {
        Write-Warning "Invalid choice. Nothing executed."
    }
}

Write-Host ""
Write-Host "=== Finished. ==="
Read-Host "Press ENTER to exit..."
