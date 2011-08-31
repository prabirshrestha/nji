# nji - Node Js (Package) Installer

This is a .net version (C#) port of https://github.com/japj/ryppi

Main differences to npm: (hopefully will fix these soon)
No version comparison, or compatibility check, only version matching to latest.
Module dependencies are all installed in to the root modules directory, in order to avoid package redundancy.

# Usage

## Installing Packages

    nji install <pkg> []<pkg>

Example:

    nji install uglify-js cssom mime express
    
## Updating Packages

    nji update

## Installing Dependencies from package.json file

    nji deps

# Nuget

You can also nji from nuget.

    Install-Package nji
