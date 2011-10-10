# nji - Node Js (Package) Installer

Nji is a package manager for NodeJs written in .NET (C#) which originally started as a port of https://github.com/japj/ryppi

Main differences to npm: (hopefully will fix these soon)
Support for partial version comparisions.
Module dependencies are all installed in to the root modules directory, in order to avoid package redundancy.

# Usage

Make sure to add nji.exe directory in the PATH environment variable.

## Installing Packages

    nji install [<pkg>]

Example:

    nji install uglify-js cssom mime express
    
## Updating Packages

    nji update

## Installing Dependencies from package.json file

    nji install

# Requirements

* .NET 4.0 Client Framework

# Nuget

You can also download nji from nuget.

    Install-Package nji

