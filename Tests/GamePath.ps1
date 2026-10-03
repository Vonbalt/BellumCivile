function Resolve-BellumGameFolder {
    param([string]$GameFolder)

    if ([string]::IsNullOrWhiteSpace($GameFolder)) { $GameFolder = $env:GameFolder }
    if ([string]::IsNullOrWhiteSpace($GameFolder)) {
        $programFiles = [Environment]::GetFolderPath('ProgramFilesX86')
        if ([string]::IsNullOrWhiteSpace($programFiles)) {
            throw 'Set the GameFolder environment variable or pass an explicit game path.'
        }
        $GameFolder = Join-Path $programFiles 'Steam\steamapps\common\Mount & Blade II Bannerlord'
    }
    return $GameFolder
}

function Resolve-BellumModuleData {
    param([string]$ModuleData)

    if ([string]::IsNullOrWhiteSpace($ModuleData)) {
        $ModuleData = Join-Path (Resolve-BellumGameFolder) 'Modules\SandBox\ModuleData'
    }
    return $ModuleData
}
