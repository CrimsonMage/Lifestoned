/*****************************************************************************************
Copyright 2018 Dereth Forever

Permission is hereby granted, free of charge, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software
without restriction, including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
*****************************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DerethForever.Web.Models.Discord
{
    public class WeenieAcceptanceEvent : IDiscordMessage
    {
        public string SubmittingUser { get; set; }

        public string AcceptingUser { get; set; }

        public DateTimeOffset AcceptanceTime { get; set; }

        public string WeenieName { get; set; }

        public uint WeenieId { get; set; }

        public string ChangelogComment { get; set; }

        public Message GetDiscordMessage()
        {
            Embed embed = Embed.GetDefault(AcceptingUser, AcceptanceTime, WeenieId);
            
            embed.Title = $"{WeenieName} - {WeenieId} UPDATED!";
            embed.Description = $"Changes were accepted by {AcceptingUser}.  Thanks you, {SubmittingUser}, for your contribution to restoring AC!\n";
            embed.Description += string.IsNullOrWhiteSpace(ChangelogComment) ? "(no change log comment)" : ChangelogComment;

            embed.Fields = new List<Field>();

            string links = "";
            links += $"[Lifestoned](http://www.lifestoned.org)";
            links += $" | [Download DF Json](http://www.lifestoned.org/Weenie/DownloadOriginalToDfJson?id={WeenieId})";
            links += $" | [Download GDLE Json](http://www.lifestoned.org/Weenie/DownloadOriginalToPhatJson?id={WeenieId})";

            embed.Fields.Add(new Field { Name = "Links", Value = links });

            return new Message(embed);
        }
    }
}
