﻿using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrmAdo.Metadata
{
    public class BooleanAttributeInfo : AttributeInfo
    {
        protected override int? GetNumericPrecision()
        {
            return 1;
        }

        protected override int? GetNumericScale()
        {
            return 0;
        }      

        public override int Length
        {
            get
            {
                return 1;
            }
            set
            {
                throw new NotImplementedException();
            }
        }      
    }


}
