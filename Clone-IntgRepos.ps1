param(
    # GitHub org
    [string]$Org = "CitiInternal",

    # Topic filter – same as in your search URL
    # https://github.com/search?q=topic%3Als-project-name-intg+org%3ACitiInternal&type=Repositories
    [string]$Topic = "ls-project-name-intg",

    # Where to clone everything
    [string]$TargetDir = "C:\INTG"
)

Write-Host "=== CitiInternal repo bulk clone ==="
Write-Host "Org        : $Org"
Write-Host "Topic      : $Topic"
Write-Host "Target dir : $TargetDir"
Write-Host ""

# ---------------------------------------------------------
# 1. Check that Git is installed
# ---------------------------------------------------------
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "Git is not installed or not in PATH. Please install Git for Windows and try again."
    Read-Host "Press ENTER to exit..."
    exit 1
}

# ---------------------------------------------------------
# 2. Make sure we can see private repos via GitHub API
#    - Use GITHUB_TOKEN env variable if present
#    - If not, ask once in this session (not stored)
# ---------------------------------------------------------
# Force TLS 1.2 for API calls
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch { }

$token = $env:GITHUB_TOKEN
if (-not $token) {
    Write-Host "GITHUB_TOKEN environment variable not found."
    Write-Host "Because CitiInternal repos are private, the script needs a GitHub Personal Access Token"
    Write-Host "(with 'repo' access and permission to access the CitiInternal org) ONLY for listing repos."
    Write-Host "Git clone itself will still use your Windows / Git credentials."
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

# ---------------------------------------------------------
# 3. Ensure target directory exists
# ---------------------------------------------------------
if (-not (Test-Path $TargetDir)) {
    Write-Host "Creating target directory: $TargetDir"
    New-Item -ItemType Directory -Path $TargetDir | Out-Null
}

# ---------------------------------------------------------
# 4. Build GitHub Search URL (same logic as your browser search)
# ---------------------------------------------------------
$perPage = 100
$page    = 1
$hasMore = $true

Write-Host "Querying GitHub for repositories..."
Write-Host ""

while ($hasMore) {
    if ([string]::IsNullOrWhiteSpace($Topic)) {
        # If you ever want all org repos (no topic), you can switch to org search like this:
        $searchUrl = "https://api.github.com/search/repositories?q=org:$Org&per_page=$perPage&page=$page"
    } else {
        # Your case: filter by topic + org
        $searchUrl = "https://api.github.com/search/repositories?q=topic:$Topic+org:$Org&per_page=$perPage&page=$page"
    }

    Write-Host "Calling GitHub search API:"
    Write-Host "  $searchUrl"
    Write-Host ""

    try {
        $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
    }
    catch {
        Write-Error "Failed to call GitHub API. Check your token and org access. Details: $_"
        break
    }

    if (-not $response.items -or $response.items.Count -eq 0) {
        Write-Host "No repositories returned for this page. Stopping."
        break
    }

    foreach ($repo in $response.items) {
        $name     = $repo.name
        $cloneUrl = $repo.clone_url   # e.g. https://github.com/CitiInternal/xyz.git
        $destPath = Join-Path $TargetDir $name

        Write-Host "----------------------------------------------------"
        Write-Host "Repo   : $name"
        Write-Host "URL    : $cloneUrl"
        Write-Host "Folder : $destPath"

        if (Test-Path $destPath) {
            Write-Host "[$name] already exists. Pulling latest changes..."
            try {
                # Uses existing Git credentials (Windows Credential Manager / GCM)
                git -C $destPath pull
            }
            catch {
                Write-Warning "Failed to pull latest for $name. Error: $_"
            }
        } else {
            Write-Host "Cloning [$name]..."
            try {
                # This will:
                # - Use stored credentials from Windows Credential Manager, OR
                # - Trigger Git Credential Manager to open browser login if not logged in
                git clone $cloneUrl $destPath
            }
            catch {
                Write-Warning "Failed to clone $name. Error: $_"
            }
        }
    }

    # Pagination handling
    $totalCount = [int]$response.total_count
    $maxPages   = [math]::Ceiling($totalCount / $perPage)

    $page++
    if ($page -gt $maxPages) {
        $hasMore = $false
    }
}

Write-Host ""
Write-Host "=== Bulk cloning finished. ==="
Read-Host "Press ENTER to exit..."
