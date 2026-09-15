# Shows the version the current commit computes - i.e. the tag name to type into the GitHub
# Release UI. Nerdbank.GitVersioning derives it from version.json plus commit height; creating the
# release does not change it, and release.yml refuses a tag that disagrees with it.
#
# NuGetPackageVersion rather than a truncation of Version: this repository ships a PRERELEASE, so
# the tag is "0.1.1-alpha" and not "0.1.1" - and a script that reported the latter would send you to
# a release the guard then rejects.
#
# Run this on up-to-date main: the version belongs to the commit you are on.
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet tool restore | Out-Null
    $tag = (dotnet tool run nbgv -- get-version --variable NuGetPackageVersion).Trim()
    $where = "$(git rev-parse --abbrev-ref HEAD) @ $(git rev-parse --short HEAD)"

    Write-Output "commit           : $where"
    Write-Output "release tag      : $tag"
    Write-Output ""
    Write-Output "Releases -> Draft a new release -> tag '$tag' -> publish; release.yml does the rest."
}
finally { Pop-Location }
