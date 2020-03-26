using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Filter : MonoBehaviour
{
    public enum FilterType
    {
        RGBRange, 
        HSVRange
    }

    public bool useFilter;
    public FilterType filterType;
    public Color filterColor;
    [Range(0,1)]
    public float filterWidth;

    public Vector4 getFilterValue()
    {
        Vector4 filterValue = new Vector4();
        switch (filterType)
        {
            case FilterType.RGBRange:
                filterValue = new Vector4(filterColor.r, filterColor.g, filterColor.b, filterWidth);
                break;
            case FilterType.HSVRange:
                float H, S, V;
                Color.RGBToHSV(filterColor, out H, out S, out V);
                filterValue = new Vector4(H, S, V, filterWidth);
                break;
            default:
                filterValue = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
                break;
        }
        return filterValue;

    }


}
