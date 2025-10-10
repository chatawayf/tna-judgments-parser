#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using UK.Gov.Legislation.Judgments;

namespace UK.Gov.Legislation.Lawmaker;

public class Metadata : IBuildable<XNode>
{

    public Dictionary<ReferenceKey, Reference> References { get; } = [];

    public static Metadata Extract(Document bill, ILogger logger) {
        string? title = "";
        try
        {
            title = Judgments.Util.Descendants<ShortTitle>(bill.CoverPage)
            .Select(title => IInline.ToString(title.Contents))
            .FirstOrDefault();
        }
        catch (Exception e)
        {
            logger.LogError(e, "error converting EMF bitmap record");
        }
        Metadata metadata = new();
        ReferenceKey key = bill.Type.IsEnacted()
            ? ReferenceKey.varActTitle
            : ReferenceKey.varBillTitle;
        metadata.References[key] = new Reference(key, title ?? "");
        return metadata;
    }


    private static XNamespace akn = XmlExt.AknNamespace;
    public XNode Build() =>
        new XElement(akn + "meta",
            new XAttribute("xmlns", akn),
            new XElement(akn + "references",
                References.Values.Select(r => r.Build())
            )
        );
}