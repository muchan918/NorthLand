using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Provides the canonical collection of tower fusion recipes.
/// </summary>
public static class TowerRecipeCatalog
{
    private const string RecipeFolder = "ScriptableObjects/TowerRecipes";

    private static IReadOnlyList<TowerRecipe> recipes;

    public static IReadOnlyList<TowerRecipe> All =>
        recipes ??= Resources.LoadAll<TowerRecipe>(RecipeFolder);
}
