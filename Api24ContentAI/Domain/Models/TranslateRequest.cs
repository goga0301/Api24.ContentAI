﻿using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace Api24ContentAI.Domain.Models
{
    public class TranslateRequest
    {
        public string Description { get; set; }
        public int LanguageId { get; set; }
        public Guid UniqueKey { get; set; }
    }
    public class EnhanceTranslateRequest
    {
        public string UserInput { get; set; }
        public string TranslateOutput { get; set; }
        public int TargetLanguageId { get; set; }
        public Guid UniqueKey { get; set; }
    }

    public class UserTranslateRequest
    {
        public string Description { get; set; }
        public int LanguageId { get; set; }
        public int SourceLanguageId { get; set; }
        public List<IFormFile> Files { get; set; }
        public bool IsPdf { get; set; }
    }
    
    public class UserTranslateRequestWithChunks
    {
        public string UserText { get; set; }
        public int LanguageId { get; set; }
        public int SourceLanguageId { get; set; }
    }
    
    public class UserTranslateEnhanceRequest
    {
        public string UserInput { get; set; }
        public string TranslateOutput { get; set; }
        public int TargetLanguageId { get; set; }
    }
}
