#!/bin/bash

# GitHub Repository Setup Script for EcSMigrationTool
# Run this script to create the repository and push the code

echo "╔═══════════════════════════════════════════════╗"
echo "║   GitHub Repository Setup                     ║"
echo "║   Repository: EcSMigerationTool              ║"
echo "╚═══════════════════════════════════════════════╝"
echo ""

# Step 1: Initialize Git repository (if not already initialized)
echo "📦 Step 1: Initializing Git repository..."
git init
echo "✓ Git repository initialized"
echo ""

# Step 2: Add all files
echo "📝 Step 2: Adding files to Git..."
git add .
echo "✓ Files staged"
echo ""

# Step 3: Create initial commit
echo "💾 Step 3: Creating initial commit..."
git commit -m "Initial commit: .NET Migration Analyzer for Windows to Linux ECS migration

- 8 comprehensive analyzers (Windows API, P/Invoke, FileSystem, Auth, Config, Packages, Quartz, CyberArk)
- 4 report formats (HTML with charts, Excel multi-sheet, JSON, Markdown)
- Full CLI with 7 options (severity filtering, exclusions, verbose mode)
- Effort estimation (developer-days calculation)
- Interactive HTML reports with Chart.js
- Comprehensive test suite
- Enterprise-grade documentation
- Sample legacy application for testing
- Roslyn-based semantic analysis
- CI/CD ready with JSON output"
echo "✓ Initial commit created"
echo ""

# Step 4: Create GitHub repository
echo "🌐 Step 4: Creating GitHub repository..."
echo ""
echo "IMPORTANT: You need to create the repository on GitHub first."
echo "Options:"
echo ""
echo "A) Using GitHub CLI (gh):"
echo "   gh repo create EcSMigerationTool --public --source=. --remote=origin --push"
echo ""
echo "B) Using GitHub Web Interface:"
echo "   1. Go to https://github.com/new"
echo "   2. Repository name: EcSMigerationTool"
echo "   3. Description: .NET Migration Analyzer - Windows VM to Linux ECS Container Assessment Tool"
echo "   4. Choose Public or Private"
echo "   5. DO NOT initialize with README, .gitignore, or license (we already have them)"
echo "   6. Click 'Create repository'"
echo ""
echo "After creating the repository, run these commands:"
echo ""
echo "   # Replace YOUR_USERNAME with your GitHub username"
echo "   git remote add origin https://github.com/YOUR_USERNAME/EcSMigerationTool.git"
echo "   git branch -M main"
echo "   git push -u origin main"
echo ""
echo "═══════════════════════════════════════════════"
echo ""
echo "Would you like to proceed with GitHub CLI? (requires 'gh' installed)"
read -p "Use GitHub CLI? (y/n): " use_gh_cli

if [ "$use_gh_cli" = "y" ] || [ "$use_gh_cli" = "Y" ]; then
    echo ""
    echo "Checking for GitHub CLI..."
    
    if command -v gh &> /dev/null; then
        echo "✓ GitHub CLI found"
        echo ""
        echo "Creating repository and pushing code..."
        
        gh repo create EcSMigerationTool \
            --public \
            --source=. \
            --remote=origin \
            --description=".NET Migration Analyzer - Windows VM to Linux ECS Container Assessment Tool" \
            --push
        
        if [ $? -eq 0 ]; then
            echo ""
            echo "✅ SUCCESS! Repository created and code pushed!"
            echo ""
            echo "🔗 Repository URL: https://github.com/$(gh api user --jq .login)/EcSMigerationTool"
            echo ""
            echo "Next steps:"
            echo "  1. View your repository in browser"
            echo "  2. Add topics: dotnet, roslyn, migration-tool, linux-containers, aws-ecs"
            echo "  3. Set up GitHub Actions (optional)"
            echo "  4. Add collaborators (optional)"
        else
            echo ""
            echo "❌ Failed to create repository. Please create it manually."
        fi
    else
        echo "❌ GitHub CLI not found."
        echo ""
        echo "Install it with:"
        echo "  macOS: brew install gh"
        echo "  Windows: winget install GitHub.cli"
        echo "  Linux: See https://cli.github.com/manual/installation"
        echo ""
        echo "Or use the web interface method described above."
    fi
else
    echo ""
    echo "Please create the repository manually using the web interface,"
    echo "then run the git commands provided above."
fi

echo ""
echo "═══════════════════════════════════════════════"
echo "Script completed!"
