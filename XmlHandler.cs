using System;
using System.Xml;
using System.Collections.Generic;

class XmlHandler
{
    private XmlDocument xml;
    private string xmlFilePath;

    /// <summary>
    /// Xml파일을 Load하고, 모듈을 초기화한다.
    /// </summary>
    /// <param name="xmlFilePath">사용할 Xml파일의 전체 경로</param>
    /// <usage>
    /// XmlHandler xmlHandler = new XmlHandler(xmlFilePath);
    /// </usage>
    public XmlHandler(string xmlFilePath)
    {
        this.xmlFilePath = xmlFilePath;

        xml = new XmlDocument
        {
            PreserveWhitespace = false
        };
        xml.Load(xmlFilePath);
    }
    /// <summary>
    /// Node 경로에 해당하는 Xml Data를 읽어, string형식으로 반환한다.
    /// </summary>
    /// <param name="firstDepthNodeName">Root노드 하위의 FirstNode</param>
    /// <param name="secondDepthNodeName">FirstNode노드 하위의 SecondNode</param>
    /// <param name="targetNodeName">SecondNode 하위의 targetNode</param>
    /// <usage>
    /// string targetData = xmlHandler.ReadXmlNode("FirstDepth", "SecondDepth", "targetNode");
    /// </usage>
    public string ReadXmlNode(string firstDepthNodeName, string secondDepthNodeName, string targetNodeName)
    {
        XmlNodeList xmlNodeList = xml.SelectNodes($"/Root/{firstDepthNodeName}");

        string targetValue = null;

        foreach (XmlNode xmlNode in xmlNodeList)
        {
            targetValue = xmlNode[$"{secondDepthNodeName}"][$"{targetNodeName}"].InnerText;
        }

        return targetValue;
    }

    public List<string> ReadXmlNodeList(string firstDepthNodeName, string secondDepthNodeName)
    {
        List<string> dataList = new List<string>();

        XmlNodeList xmlNodeList = xml.SelectNodes($"/Root/{firstDepthNodeName}/{secondDepthNodeName}");

        foreach (XmlNode xmlNode in xmlNodeList)
        {
            foreach (XmlElement xe in xmlNode)
            {
                dataList.Add(xe.InnerText);
            }
        }

        return dataList;
    }

    /// <summary>
    /// Node 경로를 받아, 해당 Xml의 Data를 생성하거나 덮어씌운다.
    /// </summary>
    /// <param name="targetContent">Xml에 추가할 데이터</param>
    /// <param name="firstDepthNodeName">Root노드 하위의 FirstNode</param>
    /// <param name="secondDepthNodeName">FirstNode노드 하위의 SecondNode</param>
    /// <param name="targetNodeName">SecondNode 하위의 targetNode</param>
    /// <usage>
    /// xmlHandler.WriteXmlNode("Data Value", "FirstDepth", "SecondDepth", "targetNode");
    /// </usage>
    public void WriteXmlNode(string targetContent, string firstDepthNodeName, string secondDepthNodeName, string targetNodeName)
    {
        XmlNode rootNode = xml.SelectSingleNode("Root");

        XmlNode firstDepthNode = rootNode.SelectSingleNode(firstDepthNodeName);

        if (firstDepthNode == null)
        {
            firstDepthNode = xml.CreateElement(firstDepthNodeName);
        }

        XmlNode secondDepthNode = firstDepthNode.SelectSingleNode(secondDepthNodeName);
        if (secondDepthNode == null)
        {
            secondDepthNode = xml.CreateElement(secondDepthNodeName);
        }

        XmlNode targetNode = secondDepthNode.SelectSingleNode(targetNodeName);
        if (targetNode == null)
        {
            targetNode = xml.CreateElement(targetNodeName);
        }

        targetNode.InnerText = targetContent;

        secondDepthNode.AppendChild(targetNode);
        firstDepthNode.AppendChild(secondDepthNode);

        xml.Save(xmlFilePath);
    }
    /// <summary>
    /// Node 경로에 해하는 Xml을 삭제한다.
    /// </summary>
    /// <param name="firstDepthNodeName">Root노드 하위의 FirstNode</param>
    /// <param name="secondDepthNodeName">FirstNode노드 하위의 SecondNode, Null이면 FirstNode까지 삭제한다.</param>
    /// <param name="valueNodeName">SecondNode 하위의 targetNode, Null이면 SecondNode까지 삭제한다.</param>
    /// <usage>
    /// xmlHandler.DeleteXmlNode("FirstDepth", "SecondDepth", "targetNode");
    /// xmlHandler.DeleteXmlNode("FirstDepth", "SecondDepth");
    /// xmlHandler.DeleteXmlNode("FirstDepth");
    /// </usage>
    public void DeleteXmlNode(string firstDepthNodeName, string secondDepthNodeName = null, string valueNodeName = null)
    {
        XmlNode rootNode = xml.SelectSingleNode("Root");
        XmlNode firstDepthNode = rootNode.SelectSingleNode(firstDepthNodeName);

        // 파라미터를 1번째 뎊스까지만 기재했다면 해당 노드와 자식을 모두 제거한다.
        if (secondDepthNodeName == null)
        {
            rootNode.RemoveAll();

            xml.Save(xmlFilePath);

            return;
        }

        XmlNode secondDepthNode = firstDepthNode.SelectSingleNode(secondDepthNodeName);

        // 파라미터를 2번째 뎊스까지만 기재했다면 해당 노드와 자식을 모두 제거한다.
        if (valueNodeName == null)
        {
            firstDepthNode.RemoveChild(secondDepthNode);

            xml.Save(xmlFilePath);

            return;
        }
        else
        {
            // 모두 기재했을 경우
            XmlNode valueNode = secondDepthNode.SelectSingleNode(valueNodeName);

            Console.WriteLine(valueNode == null);

            secondDepthNode.RemoveChild(valueNode);
            xml.Save(xmlFilePath);

            return;
        }
    }
}
